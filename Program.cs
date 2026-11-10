using System.Numerics;
using Raylib_cs;
using VoiceCarSim;
using VoiceCarSim.Control;
using VoiceCarSim.Rendering;
using VoiceCarSim.Simulation;
using VoiceCarSim.Voice;

// =====================================================================
//  Симулятор машинки с голосовым управлением.
//
//  Структура:
//    Voice/VoiceRecognizer      — микрофон + распознавание речи (Vosk)
//    Control/CommandParser      — слово → команда
//    Control/CarController — логика и движение (не зависит от графики)
//    Simulation/World           — комната, препятствия, датчики расстояния
//    Rendering/*                — 3D-сцена и интерфейс
// =====================================================================

var startPosition = World.StartPosition;

Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
Raylib.InitWindow(1280, 760, "Голосовое управление машинкой — симулятор");
Raylib.SetTargetFPS(60);
// Ближняя и дальняя плоскости отсечения камеры: ближняя 0,05 м вместо 0,01 м
// заметно повышает точность буфера глубины на большом полигоне
Rlgl.SetClipPlanes(0.05, 400.0);
// Рисовать обе стороны треугольников (у кузова есть плоские панели)
Rlgl.DisableBackfaceCulling();
Raylib.SetExitKey(KeyboardKey.Null);  // Esc не закрывает окно случайно

var world = new World();
var car = new CarController(startPosition);
var scene = new SceneRenderer();
using var hud = new Hud();
using var log = new CommandLog();

car.Notify += message =>
{
    hud.AddEvent(message);
    log.Event(message);
};

// --- Запуск распознавания речи ---
VoiceRecognizer? voice = null;
string micStatus;
string? modelPath = FindModel(args);
if (modelPath == null)
{
    micStatus = "Нет модели Vosk — только клавиатура";
    hud.AddEvent("Папка model не найдена (см. README)");
}
else
{
    try
    {
        voice = new VoiceRecognizer(modelPath);
        voice.Start();
        micStatus = "Микрофон: слушаю";
        hud.AddEvent("Модель загружена, говорите команды");
    }
    catch (Exception ex)
    {
        voice = null;
        micStatus = "Ошибка микрофона — только клавиатура";
        hud.AddEvent($"Ошибка: {ex.Message}");
    }
}

// --- Камера ---
var camera = new Camera3D
{
    Position = new Vector3(0, 3, -10),
    Target = startPosition,
    Up = Vector3.UnitY,
    FovY = 55f,
    Projection = CameraProjection.Perspective,
};
string[] cameraNames = { "сзади", "сверху", "обзор" };
int cameraMode = 0;
float zoom = 1f;  // колесо мыши: приблизить / отдалить
bool wasOnRoad = true;

// --- Главный цикл ---
while (!Raylib.WindowShouldClose())
{
    float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);

    // 1. Голосовые команды из фонового потока
    if (voice != null)
    {
        while (voice.Commands.TryDequeue(out var vc))
        {
            log.Command(vc.Time, vc.FromPartial ? "голос (быстрый)" : "голос", vc.Word, vc.Confidence);
            Execute(vc.Command, $"«{vc.Word}» ({vc.Confidence:0.00})");
        }
        while (voice.Ignored.TryDequeue(out var phrase))
        {
            log.Command(DateTime.Now, "голос (не команда)", phrase, 0f);
            hud.AddEvent($"Не команда: «{(phrase.Length > 30 ? phrase[..30] + "…" : phrase)}»");
        }
    }

    // 2. Клавиатура: для отладки и как запасной вариант на защите
    foreach (var (key, cmd) in KeyMap)
    {
        if (Raylib.IsKeyPressed(key))
        {
            log.Command(DateTime.Now, "клавиатура", CommandParser.ToRussian(cmd), 1f);
            Execute(cmd, $"[{key}]");
        }
    }
    if (Raylib.IsKeyPressed(KeyboardKey.R))
    {
        car.Reset(startPosition);
        scene.ClearTrail();
        hud.AddEvent("Сброс положения");
    }
    if (Raylib.IsKeyPressed(KeyboardKey.C)) cameraMode = (cameraMode + 1) % cameraNames.Length;
    zoom = Math.Clamp(zoom * (1f - Raylib.GetMouseWheelMove() * 0.1f), 0.3f, 4f);
    if (Raylib.IsKeyPressed(KeyboardKey.F1)) hud.ShowHelp = !hud.ShowHelp;
    if (Raylib.IsKeyPressed(KeyboardKey.M) && voice != null)
    {
        voice.Muted = !voice.Muted;
        micStatus = voice.Muted ? "Микрофон выключен (M)" : "Микрофон: слушаю";
    }

    // 3. Датчики и движение
    float front = world.Sense(SceneRenderer.SensorOrigin(car, true), car.Yaw);
    float rear = world.Sense(SceneRenderer.SensorOrigin(car, false), car.Yaw + 180f);
    car.Update(dt, front, rear, p => world.Collides(p, Settings.CarRadius));

    // Контроль дороги: сообщаем, когда машинка съехала и когда вернулась
    bool onRoad = world.IsOnRoad(car.Position);
    if (onRoad != wasOnRoad)
    {
        string msg = onRoad ? "Вернулась на дорогу" : "Съехала с дороги!";
        hud.AddEvent(msg);
        log.Event(msg);
        wasOnRoad = onRoad;
    }

    // 4. Камера
    UpdateCamera(dt);

    // 5. Отрисовка
    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(200, 214, 228, 255));
    Raylib.BeginMode3D(camera);
    scene.Draw(world, car, front, rear);
    Raylib.EndMode3D();
    hud.Draw(car, front, rear, wasOnRoad, micStatus, voice is { Muted: false }, voice?.InputLevel ?? 0f,
             voice?.Partial ?? "", cameraNames[cameraMode]);
    Raylib.EndDrawing();
}

voice?.Dispose();
Raylib.CloseWindow();


// =====================================================================

void Execute(Command command, string source)
{
    hud.FlashCommand(CommandParser.ToRussian(command));
    hud.AddEvent($"Команда {source}");
    car.Handle(command);
}

void UpdateCamera(float dt)
{
    Vector3 desiredPos, desiredTarget;
    var p = car.Position;
    switch (cameraMode)
    {
        case 0: // от третьего лица, за спиной
            desiredPos = p + (-car.Forward * 3.5f + new Vector3(0, 2.2f, 0)) * zoom;
            desiredTarget = p + car.Forward * 1.5f + new Vector3(0, 0.5f, 0);
            break;
        case 1: // сверху — удобно смотреть на след и углы поворота
            desiredPos = p + new Vector3(0, 18f * zoom, -0.01f);
            desiredTarget = p;
            break;
        default: // неподвижная камера на весь полигон
            desiredPos = new Vector3(0, 70f, -75f) * zoom;
            desiredTarget = Vector3.Zero;
            break;
    }
    float k = 1f - MathF.Exp(-6f * dt);  // плавное следование
    camera.Position = Vector3.Lerp(camera.Position, desiredPos, k);
    camera.Target = Vector3.Lerp(camera.Target, desiredTarget, k);
}

/// <summary>
/// Ищет папку с моделью Vosk: путь из аргумента командной строки
/// или папка «model» рядом с программой / в папке проекта.
/// </summary>
static string? FindModel(string[] args)
{
    if (args.Length > 0 && Directory.Exists(args[0])) return args[0];

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "model");
        if (Directory.Exists(Path.Combine(candidate, "am"))) return candidate;

        // если архив распаковался с вложенной папкой vosk-model-...
        if (Directory.Exists(candidate))
        {
            var nested = Directory.GetDirectories(candidate)
                .FirstOrDefault(d => Directory.Exists(Path.Combine(d, "am")));
            if (nested != null) return nested;
        }
    }
    return null;
}

partial class Program
{
    static readonly (KeyboardKey Key, Command Cmd)[] KeyMap =
    {
        (KeyboardKey.W, Command.Forward),
        (KeyboardKey.S, Command.Backward),
        (KeyboardKey.G, Command.Gas),
        (KeyboardKey.Space, Command.Stop),
        (KeyboardKey.B, Command.Brake),
        (KeyboardKey.A, Command.Left),
        (KeyboardKey.D, Command.Right),
        (KeyboardKey.Q, Command.Lefter),
        (KeyboardKey.E, Command.Righter),
    };
}
