namespace VoiceCarSim.Control;

public enum Command
{
    Forward,   // «прямо»
    Backward,  // «назад»
    Gas,       // «газ»
    Stop,      // «стоп»
    Brake,     // «тормоз»: скорость −5 км/ч
    Right,     // «направо»: поворот на месте на 90°
    Left,      // «налево»
    Righter,   // «правее»: подруливание на 5°
    Lefter,    // «левее»
}

public static class CommandParser
{
    /// <summary>Словарь распознавателя. Vosk будет выбирать только из этих слов.</summary>
    public static readonly string[] Vocabulary =
    {
        "прямо", "назад", "газ", "тормоз", "стоп",
        "направо", "налево", "правее", "левее",
    };

    public static bool TryParse(string word, out Command command)
    {
        switch (word.Trim().ToLowerInvariant().Replace('ё', 'е'))
        {
            case "прямо": command = Command.Forward; return true;
            case "назад": command = Command.Backward; return true;
            case "газ": command = Command.Gas; return true;
            case "стоп": command = Command.Stop; return true;
            case "тормоз": command = Command.Brake; return true;
            case "направо": command = Command.Right; return true;
            case "налево": command = Command.Left; return true;
            case "правее": command = Command.Righter; return true;
            case "левее": command = Command.Lefter; return true;
            default: command = default; return false;
        }
    }

    public static string ToRussian(Command c) => c switch
    {
        Command.Forward => "прямо",
        Command.Backward => "назад",
        Command.Gas => "газ",
        Command.Stop => "стоп",
        Command.Brake => "тормоз",
        Command.Right => "направо",
        Command.Left => "налево",
        Command.Righter => "правее",
        Command.Lefter => "левее",
        _ => c.ToString(),
    };
}
