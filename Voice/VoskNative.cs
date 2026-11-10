using System.Runtime.InteropServices;

namespace VoiceCarSim.Voice;

/// <summary>
/// Прямые вызовы библиотеки libvosk (она ставится вместе с NuGet-пакетом Vosk).
///
/// Зачем не использовать готовые классы Vosk.Model / Vosk.VoskRecognizer:
/// они передают строки в библиотеку в кодировке Windows (cp1251), а Vosk ждёт UTF-8.
/// Из-за этого русские слова словаря и пути с русскими буквами доходят искажёнными,
/// и распознаватель выдаёт только "[unk]". Здесь все строки передаются в UTF-8 явно.
/// </summary>
internal static class VoskNative
{
    private const string Lib = "libvosk";

    [DllImport(Lib, EntryPoint = "vosk_set_log_level")]
    public static extern void SetLogLevel(int level);

    [DllImport(Lib, EntryPoint = "vosk_model_new")]
    public static extern IntPtr ModelNew([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath);

    [DllImport(Lib, EntryPoint = "vosk_model_free")]
    public static extern void ModelFree(IntPtr model);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_new")]
    public static extern IntPtr RecognizerNew(IntPtr model, float sampleRate);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_new_grm")]
    public static extern IntPtr RecognizerNewGrm(IntPtr model, float sampleRate,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string grammar);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_set_words")]
    public static extern void RecognizerSetWords(IntPtr recognizer, int words);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_accept_waveform")]
    public static extern int RecognizerAcceptWaveform(IntPtr recognizer, byte[] data, int length);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_result")]
    private static extern IntPtr RecognizerResultPtr(IntPtr recognizer);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_partial_result")]
    private static extern IntPtr RecognizerPartialResultPtr(IntPtr recognizer);

    [DllImport(Lib, EntryPoint = "vosk_recognizer_free")]
    public static extern void RecognizerFree(IntPtr recognizer);

    public static string Result(IntPtr recognizer)
        => Marshal.PtrToStringUTF8(RecognizerResultPtr(recognizer)) ?? "{}";

    public static string PartialResult(IntPtr recognizer)
        => Marshal.PtrToStringUTF8(RecognizerPartialResultPtr(recognizer)) ?? "{}";
}
