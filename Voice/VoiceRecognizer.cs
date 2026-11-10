using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using NAudio.Wave;
using VoiceCarSim.Control;

namespace VoiceCarSim.Voice;

/// <summary>Распознанное слово-команда.</summary>
public readonly record struct VoiceCommand(Command Command, string Word, float Confidence, bool FromPartial, DateTime Time);

/// <summary>
/// Распознавание голосовых команд офлайн: микрофон (NAudio) → Vosk.
///
/// Защита от ложных срабатываний на обычную речь:
///  1. Vosk распознаёт речь свободно, как обычный текст (без словаря из 8 слов),
///     поэтому посторонняя фраза остаётся сама собой, а не «подгоняется» под команду.
///  2. Команда принимается, только если вся фраза — это ровно одно слово-команда
///     (плюс кодовое слово, если оно включено в Settings.WakeWord).
///  3. Исключение — «стоп»: срабатывает, даже если прозвучало внутри фразы.
///     Лишняя остановка безопасна, пропущенная — нет.
///
/// Аудио обрабатывается в фоновом потоке, команды складываются в очередь,
/// которую главный цикл разбирает каждый кадр.
/// </summary>
public sealed class VoiceRecognizer : IDisposable
{
    public ConcurrentQueue<VoiceCommand> Commands { get; } = new();

    /// <summary>Фразы, которые были услышаны, но не приняты как команда.</summary>
    public ConcurrentQueue<string> Ignored { get; } = new();

    /// <summary>Что сейчас слышно (промежуточный результат), для вывода на экран.</summary>
    public string Partial { get; private set; } = "";
    public float InputLevel { get; private set; }  // громкость 0..1, для индикатора
    public bool IsListening { get; private set; }
    public bool Muted { get; set; }

    private IntPtr _model;
    private IntPtr _recognizer;
    private readonly WaveInEvent _waveIn;
    private readonly object _lock = new();
    private bool _stopFiredInUtterance;

    public VoiceRecognizer(string modelPath)
    {
        VoskNative.SetLogLevel(0);
        _model = VoskNative.ModelNew(modelPath);
        if (_model == IntPtr.Zero)
            throw new InvalidOperationException($"Vosk не смог загрузить модель из «{modelPath}»");

        if (Settings.UseGrammar)
        {
            // Режим со словарём: точнее на самих командах, но любую речь подгоняет под них.
            // Русские буквы передаём как есть: Vosk не понимает экранирование вида \u0432.
            var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            var words = CommandParser.Vocabulary.Append("[unk]");
            if (Settings.WakeWord.Length > 0) words = words.Append(Settings.WakeWord);
            var grammar = JsonSerializer.Serialize(words, jsonOptions);
            _recognizer = VoskNative.RecognizerNewGrm(_model, Settings.SampleRate, grammar);
        }
        else
        {
            _recognizer = VoskNative.RecognizerNew(_model, Settings.SampleRate);
        }
        if (_recognizer == IntPtr.Zero)
            throw new InvalidOperationException("Vosk не смог создать распознаватель");
        VoskNative.RecognizerSetWords(_recognizer, 1);  // нужна уверенность (conf) по каждому слову

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(Settings.SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += OnAudio;
    }

    public void Start()
    {
        _waveIn.StartRecording();
        IsListening = true;
    }

    private void OnAudio(object? sender, WaveInEventArgs e)
    {
        InputLevel = ComputeLevel(e.Buffer, e.BytesRecorded);
        if (Muted) return;

        lock (_lock)
        {
            if (_recognizer == IntPtr.Zero) return;
            if (VoskNative.RecognizerAcceptWaveform(_recognizer, e.Buffer, e.BytesRecorded) != 0)
            {
                HandleFinal(VoskNative.Result(_recognizer));
                Partial = "";
            }
            else
            {
                HandlePartial(VoskNative.PartialResult(_recognizer));
            }
        }
    }

    /// <summary>
    /// Быстрый путь для «стоп»: реагируем уже на промежуточный результат,
    /// не дожидаясь паузы после фразы. Это экономит около полусекунды.
    /// </summary>
    private void HandlePartial(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Partial = doc.RootElement.TryGetProperty("partial", out var p) ? p.GetString() ?? "" : "";
        Partial = Partial.Replace("[unk]", "…");

        if (!_stopFiredInUtterance && Normalize(Partial).Split(' ').Contains("стоп"))
        {
            _stopFiredInUtterance = true;
            Commands.Enqueue(new VoiceCommand(Command.Stop, "стоп", 1f, true, DateTime.Now));
        }
    }

    private void HandleFinal(string json)
    {
        using var doc = JsonDocument.Parse(json);
        bool stopAlreadyFired = _stopFiredInUtterance;
        _stopFiredInUtterance = false;

        // Слова фразы с уверенностью распознавания
        var words = new List<(string Word, float Conf)>();
        if (doc.RootElement.TryGetProperty("result", out var arr))
        {
            foreach (var w in arr.EnumerateArray())
            {
                string word = Normalize(w.GetProperty("word").GetString() ?? "");
                float conf = w.TryGetProperty("conf", out var c) ? c.GetSingle() : 1f;
                if (word.Length > 0) words.Add((word, conf));
            }
        }
        if (words.Count == 0) return;  // тишина или шум
        string phrase = string.Join(' ', words.Select(w => w.Word));

        // «Стоп» — в любом месте фразы
        if (words.Any(w => w.Word == "стоп"))
        {
            if (!stopAlreadyFired)
                Commands.Enqueue(new VoiceCommand(Command.Stop, "стоп", 1f, false, DateTime.Now));
            return;
        }

        // Кодовое слово, если включено: «машина прямо»
        if (Settings.WakeWord.Length > 0)
        {
            if (words[0].Word != Normalize(Settings.WakeWord)) { Ignored.Enqueue(phrase); return; }
            words.RemoveAt(0);
        }

        // Остальные команды — только если фраза состоит ровно из одного слова-команды
        if (words.Count == 1
            && CommandParser.TryParse(words[0].Word, out var command)
            && words[0].Conf >= Settings.MinConfidence)
        {
            Commands.Enqueue(new VoiceCommand(command, words[0].Word, words[0].Conf, false, DateTime.Now));
        }
        else
        {
            Ignored.Enqueue(phrase);
        }
    }

    private static string Normalize(string s) => s.Trim().ToLowerInvariant().Replace('ё', 'е');

    private static float ComputeLevel(byte[] buffer, int bytes)
    {
        int peak = 0;
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            int sample = Math.Abs((int)BitConverter.ToInt16(buffer, i));
            if (sample > peak) peak = sample;
        }
        return peak / 32768f;
    }

    public void Dispose()
    {
        IsListening = false;
        try { _waveIn.StopRecording(); } catch { /* устройство уже отключено */ }
        _waveIn.Dispose();
        lock (_lock)
        {
            if (_recognizer != IntPtr.Zero) VoskNative.RecognizerFree(_recognizer);
            if (_model != IntPtr.Zero) VoskNative.ModelFree(_model);
            _recognizer = _model = IntPtr.Zero;
        }
    }
}
