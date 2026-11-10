using System.Numerics;
using Raylib_cs;
using VoiceCarSim.Control;

namespace VoiceCarSim.Rendering;

/// <summary>Двумерный интерфейс поверх 3D-сцены: состояние, журнал, подсказки.</summary>
public sealed class Hud : IDisposable
{
    private readonly Font _font;
    private readonly bool _ownsFont;
    private readonly List<(DateTime Time, string Text)> _events = new();
    private string _flash = "";
    private double _flashTime = -10;

    public bool ShowHelp { get; set; } = true;

    private static readonly Color Panel = new(20, 24, 32, 200);
    private static readonly Color TextMain = new(240, 242, 245, 255);
    private static readonly Color TextDim = new(160, 168, 180, 255);
    private static readonly Color Accent = new(90, 200, 120, 255);
    private static readonly Color Warning = new(240, 90, 80, 255);

    public Hud()
    {
        // Стандартный шрифт raylib не содержит кириллицы, поэтому берём системный.
        string[] candidates =
        {
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path != null)
        {
            var codepoints = new List<int>();
            for (int c = 32; c < 127; c++) codepoints.Add(c);
            for (int c = 0x400; c < 0x460; c++) codepoints.Add(c);   // кириллица
            codepoints.AddRange(new[] { '°', '«', '»', '—', '–', '·', '→', '●', '…', '±', '∞' }.Select(ch => (int)ch));
            _font = Raylib.LoadFontEx(path, 48, codepoints.ToArray(), codepoints.Count);
            Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
            _ownsFont = true;
        }
        else
        {
            _font = Raylib.GetFontDefault();
        }
    }

    public void AddEvent(string text)
    {
        _events.Add((DateTime.Now, text));
        if (_events.Count > 9) _events.RemoveAt(0);
    }

    /// <summary>Крупная надпись распознанной команды по центру экрана.</summary>
    public void FlashCommand(string word)
    {
        _flash = word.ToUpperInvariant();
        _flashTime = Raylib.GetTime();
    }

    public void Draw(CarController car, float front, float rear, bool onRoad,
                     string micStatus, bool micOk, float micLevel, string partial, string cameraName)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();

        // --- Состояние машинки (слева сверху) ---
        Raylib.DrawRectangleRounded(new Rectangle(12, 12, 360, 318), 0.08f, 8, Panel);
        float y = 22;
        Text("Состояние", 20, y, 22, TextDim); y += 30;
        Text(ModeName(car.Mode), 20, y, 30, TextMain); y += 38;
        Text($"Скорость: {MathF.Abs(car.SpeedKmh):0.0} км/ч" +
             (car.SetpointKmh > 0 ? $"  (задано {car.SetpointKmh:0})" : ""), 20, y, 22, TextMain); y += 28;
        float targetCourse = ((-car.TargetYaw) % 360 + 360) % 360;
        Text($"Курс: {car.CourseDeg:0}°" +
             (MathF.Abs(car.TargetYaw - car.Yaw) > 0.1f ? $"  → {targetCourse:0}°" : ""), 20, y, 22, TextMain); y += 28;
        Text($"Датчик спереди: {Dist(front)}", 20, y, 22, front < 1.5f ? Warning : TextMain); y += 28;
        Text($"Датчик сзади:   {Dist(rear)}", 20, y, 22, rear < 1.5f ? Warning : TextMain); y += 28;
        Text(onRoad ? "Дорога: на дороге" : "Дорога: СЪЕХАЛА!", 20, y, 22, onRoad ? Accent : Warning); y += 28;
        Text($"Пройдено: {car.Odometer:0.0} м", 20, y, 22, TextMain); y += 28;
        Text("Тормозной путь: " + (car.LastBrakingDistance is float bd ? $"{bd:0.0} м" : "—"), 20, y, 22, TextMain);

        // --- Микрофон ---
        Raylib.DrawRectangleRounded(new Rectangle(12, 340, 360, 96), 0.1f, 8, Panel);
        Raylib.DrawCircle(30, 366, 7, micOk ? Accent : Warning);
        Text(micStatus, 46, 354, 22, TextMain);
        Raylib.DrawRectangle(20, 386, 340, 8, new Color(95, 100, 112, 255));
        Raylib.DrawRectangle(20, 386, (int)(340 * Math.Clamp(micLevel * 3f, 0f, 1f)), 8, Accent);
        Text(partial.Length > 0 ? $"Слышу: «{partial}»" : "Слышу: …", 20, 402, 20, TextDim);

        // --- Журнал событий (справа сверху) ---
        int logW = 440;
        Raylib.DrawRectangleRounded(new Rectangle(w - logW - 12, 12, logW, 40 + _events.Count * 26), 0.05f, 8, Panel);
        Text($"Журнал    камера: {cameraName} (C)", w - logW, 22, 20, TextDim);
        for (int i = 0; i < _events.Count; i++)
        {
            var (t, text) = _events[i];
            Text($"{t:HH:mm:ss}  {text}", w - logW, 50 + i * 26, 19,
                text.Contains("Препятствие") || text.Contains("Столкновение") || text.Contains("Съехала") ? Warning
                : text.StartsWith("Не команда") ? TextDim : TextMain);
        }

        // --- Последняя команда крупно ---
        double age = Raylib.GetTime() - _flashTime;
        if (age < 1.2)
        {
            byte alpha = (byte)(255 * (1 - age / 1.2));
            var size = Raylib.MeasureTextEx(_font, _flash, 64, 2);
            Raylib.DrawTextEx(_font, _flash, new Vector2((w - size.X) / 2, h * 0.18f), 64, 2,
                new Color((byte)255, (byte)255, (byte)255, alpha));
        }

        // --- Подсказка по командам ---
        if (ShowHelp)
        {
            string[] lines =
            {
                "Голос: «прямо», «назад», «газ», «тормоз», «стоп», «направо», «налево», «правее», «левее»",
                "Клавиши: W прямо · S назад · G газ · B тормоз · Пробел стоп · A/D налево/направо · Q/E левее/правее",
                "M микрофон вкл/выкл · C камера · колесо мыши — масштаб · R сброс · F1 скрыть подсказку",
            };
            int panelH = 20 + lines.Length * 26;
            Raylib.DrawRectangleRounded(new Rectangle(12, h - panelH - 12, w - 24, panelH), 0.1f, 8, Panel);
            for (int i = 0; i < lines.Length; i++)
                Text(lines[i], 24, h - panelH + i * 26, 20, i == 0 ? TextMain : TextDim);
        }
    }

    private static string ModeName(MotionMode m) => m switch
    {
        MotionMode.Forward => "Едет вперёд",
        MotionMode.Backward => "Едет назад",
        MotionMode.Turning => "Поворот на месте",
        _ => "Стоит",
    };

    private static string Dist(float d) => float.IsInfinity(d) ? "свободно" : $"{d:0.0} м";

    private void Text(string s, float x, float y, float size, Color color)
        => Raylib.DrawTextEx(_font, s, new Vector2(x, y), size, 1, color);

    public void Dispose()
    {
        if (_ownsFont) Raylib.UnloadFont(_font);
    }
}
