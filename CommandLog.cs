using System.Globalization;
using System.Text;

namespace VoiceCarSim;

/// <summary>
/// Журнал сессии в CSV (открывается в Excel). Пригодится для раздела
/// «Испытания» курсовой: сколько команд распознано, с какой уверенностью,
/// что сделала машинка.
/// </summary>
public sealed class CommandLog : IDisposable
{
    private readonly StreamWriter? _writer;
    public string? FilePath { get; }

    public CommandLog()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            // UTF-8 с BOM, чтобы Excel правильно показал кириллицу
            _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true)) { AutoFlush = true };
            _writer.WriteLine("время;источник;слово;уверенность;событие");
        }
        catch
        {
            _writer = null;  // журнал не обязателен для работы
        }
    }

    public void Command(DateTime time, string source, string word, float confidence)
        => Write(time, source, word, confidence.ToString("0.00", CultureInfo.InvariantCulture), "");

    public void Event(string message) => Write(DateTime.Now, "система", "", "", message);

    private void Write(DateTime t, string source, string word, string conf, string message)
        => _writer?.WriteLine($"{t:HH:mm:ss.fff};{source};{word};{conf};{message.Replace(';', ',')}");

    public void Dispose() => _writer?.Dispose();
}
