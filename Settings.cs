namespace VoiceCarSim;

/// <summary>
/// Все настраиваемые параметры симулятора в одном месте.
/// Единицы: метры, секунды, градусы, км/ч.
/// </summary>
public static class Settings
{
    // --- Скорость ---
    public const float BaseSpeedKmh = 5f;      // скорость после команды «прямо»
    public const float SpeedStepKmh = 5f;      // прибавка за одну команду «газ»
    public const float BrakeStepKmh = 5f;      // убавка за одну команду «тормоз»
    public const float MaxForwardKmh = 15f;    // потолок скорости вперёд
    public const float ReverseSpeedKmh = 3f;   // скорость после команды «назад»
    public const float MaxReverseKmh = 5f;     // потолок скорости назад

    // --- Плавность ---
    public const float Acceleration = 1.5f;    // м/с², разгон
    public const float Deceleration = 2.5f;    // м/с², обычное замедление
    public const float StopDeceleration = 4f;  // м/с², команда «стоп»
    public const float EmergencyDeceleration = 6f; // м/с², аварийная остановка

    // --- Повороты ---
    public const float BigTurnDeg = 90f;       // «направо» / «налево»: поворот на месте
    public const float SmallTurnDeg = 5f;      // «правее» / «левее»: подруливание
    public const float TurnRateInPlace = 60f;  // град/с, поворот на месте
    public const float TurnRateMoving = 30f;   // град/с, подруливание на ходу

    // --- Датчики препятствий (имитация ультразвуковых) ---
    public const float SensorRange = 6f;       // м, дальность датчика
    public const float SafetyMargin = 0.6f;    // м, запас до препятствия

    // --- Геометрия машинки ---
    public const float CarRadius = 0.85f;    // м, для проверки столкновений
    public const float WheelRadius = 0.17f;    // м, радиус ведущего колеса (звёздочки) гусеницы
    public const float TrackWidth = 0.74f;     // м, расстояние между центрами левой и правой гусениц

    // --- Распознавание речи ---
    public const int SampleRate = 16000;
    public const float MinConfidence = 0.6f;   // слова с меньшей уверенностью отбрасываются

    // false — Vosk распознаёт речь свободно, и обычные фразы не превращаются в команды.
    // true  — Vosk выбирает только из 8 слов-команд: чуть точнее на командах,
    //         но любую речь «подгоняет» под них (больше ложных срабатываний).
    public static readonly bool UseGrammar = false;

    // Кодовое слово перед командой, например "машина" → «машина, прямо».
    // Пустая строка — кодовое слово не нужно. «Стоп» работает всегда и без него.
    public static readonly string WakeWord = "";

    public static float KmhToMs(float kmh) => kmh / 3.6f;
    public static float MsToKmh(float ms) => ms * 3.6f;
}
