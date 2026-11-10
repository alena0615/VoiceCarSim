using System.Numerics;
using Raylib_cs;
using VoiceCarSim.Control;
using VoiceCarSim.Simulation;

namespace VoiceCarSim.Rendering;

/// <summary>Рисует траву, дорогу, ограждение, след и 3D-модель машинки из простых фигур.</summary>
public sealed class SceneRenderer
{
    private readonly List<Vector3> _trail = new();

    public void ClearTrail() => _trail.Clear();

    public void Draw(World world, CarController car, float frontDistance, float rearDistance)
    {
        // --- Слой пола ---
        // Трава, дорога, разметка и след лежат на одной высоте. Если сравнивать их
        // по глубине, видеокарта путается, что ближе, и разметка «скачет» (z-fighting).
        // Поэтому пол рисуем слоями по порядку без проверки глубины: каждый следующий
        // слой ложится поверх предыдущего. Пол ниже всех предметов, так что машинка
        // и ограждение, нарисованные после, корректно его перекрывают.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();

        Raylib.DrawPlane(Vector3.Zero, new Vector2(World.RoomSize, World.RoomSize), new Color(112, 150, 82, 255));

        // Дорога: асфальт, края, осевая, отметки — в порядке списка
        foreach (var q in world.FloorQuads)
        {
            var a = new Vector3(q.A.X, 0, q.A.Y);
            var b = new Vector3(q.B.X, 0, q.B.Y);
            var c = new Vector3(q.C.X, 0, q.C.Y);
            var d = new Vector3(q.D.X, 0, q.D.Y);
            Raylib.DrawTriangle3D(a, b, c, q.Color);
            Raylib.DrawTriangle3D(a, c, d, q.Color);
        }

        DrawTrail(car.Position);

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();

        // --- Объёмные объекты (с обычной проверкой глубины) ---
        foreach (var o in world.Obstacles)
        {
            var center = new Vector3(o.Center.X, o.Height / 2f, o.Center.Y);
            Raylib.DrawCube(center, o.Size.X, o.Height, o.Size.Y, o.Color);
            Raylib.DrawCubeWires(center, o.Size.X, o.Height, o.Size.Y, new Color(90, 70, 50, 255));
        }

        DrawHeadingLines(car);
        DrawSensor(car, frontDistance, true);
        DrawSensor(car, rearDistance, false);
        DrawCar(car);
    }

    private void DrawTrail(Vector3 position)
    {
        var p = position with { Y = 0.01f };
        if (_trail.Count == 0 || Vector3.Distance(_trail[^1], p) > 0.1f)
        {
            _trail.Add(p);
            if (_trail.Count > 20000) _trail.RemoveRange(0, 1000);
        }
        for (int i = 1; i < _trail.Count; i++)
            Raylib.DrawLine3D(_trail[i - 1], _trail[i], new Color(220, 60, 60, 255));
    }

    /// <summary>Зелёная линия — куда смотрит машинка, жёлтая — заданный курс.</summary>
    private static void DrawHeadingLines(CarController car)
    {
        var start = (car.Position + car.Forward * HalfLength) with { Y = 0.03f };
        Raylib.DrawLine3D(start, start + car.Forward * 4f, new Color(30, 200, 60, 255));
        if (MathF.Abs(car.TargetYaw - car.Yaw) > 0.1f)
            Raylib.DrawLine3D(start, start + CarController.DirectionFromYaw(car.TargetYaw) * 4f,
                new Color(240, 190, 0, 255));
    }

    private static void DrawSensor(CarController car, float distance, bool front)
    {
        if (float.IsInfinity(distance)) return;
        float yaw = front ? car.Yaw : car.Yaw + 180f;
        var dir = CarController.DirectionFromYaw(yaw);
        var origin = SensorOrigin(car, front) with { Y = 0.3f };
        var color = distance < 1.5f ? new Color(230, 40, 40, 255) : new Color(240, 150, 30, 255);
        Raylib.DrawLine3D(origin, origin + dir * distance, color);
        Raylib.DrawSphere(origin + dir * distance, 0.05f, color);
    }

    /// <summary>Где на машинке стоит датчик (на переднем или заднем бампере).</summary>
    public static Vector3 SensorOrigin(CarController car, bool front)
        => car.Position + car.Forward * (front ? HalfLength : -HalfLength);

    // ------------------------------------------------------------------
    //  3D-модель: чёрное спортивное купе на гусеничной раме.
    //  Локальные координаты: смотрит вдоль +Z, левая сторона +X, пол на Y = 0.
    //
    //  Управление как у набора 4WD: левая гусеница и правая крутятся
    //  каждая со своей скоростью, поворот — за счёт разницы скоростей.
    // ------------------------------------------------------------------

    private const float HalfLength = 0.84f;                 // до трубчатых бамперов

    // Гусеница
    private const float TrackHalfBase = 0.55f;              // от центра до осей звёздочки и ленивца
    private const float TrackR = Settings.WheelRadius;      // радиус звёздочки / ленивца
    private const float TrackBeltWidth = 0.18f;
    private const float TrackCenterY = TrackR + 0.015f;     // высота осей: низ ленты чуть над полом
    private const int TrackLinks = 36;
    private static readonly float TrackPerimeter = 4f * TrackHalfBase + 2f * MathF.PI * TrackR;

    private static readonly Color BodyColor = new(22, 22, 26, 255);
    private static readonly Color BodyEdge = new(70, 72, 80, 255);
    private static readonly Color GlassColor = new(48, 60, 78, 255);
    private static readonly Color FrameColor = new(40, 42, 40, 255);
    private static readonly Color LinkColor = new(38, 38, 40, 255);
    private static readonly Color RollerColor = new(60, 64, 58, 255);
    private static readonly Color SpringColor = new(150, 150, 140, 255);
    private static readonly Color Chrome = new(180, 185, 195, 255);

    private static void DrawCar(CarController car)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(car.Position.X, 0f, car.Position.Z);
        Rlgl.Rotatef(car.Yaw, 0f, 1f, 0f);

        const float trackX = Settings.TrackWidth / 2f;

        // --- Ходовая часть ---
        DrawTrack(trackX, car.LeftTrackDistance, car.LeftWheelAngle);
        DrawTrack(-trackX, car.RightTrackDistance, car.RightWheelAngle);

        // Рама: продольные лонжероны и поперечины
        foreach (float side in new[] { -1f, 1f })
            Tube(new(side * 0.24f, 0.37f, -0.78f), new(side * 0.24f, 0.37f, 0.78f), 0.025f, FrameColor);
        foreach (float z in new[] { -0.55f, 0f, 0.55f })
            Tube(new(-trackX, 0.37f, z), new(trackX, 0.37f, z), 0.02f, FrameColor);

        // Трубчатые бамперы спереди и сзади
        foreach (float end in new[] { -1f, 1f })
        {
            float z = end * 0.82f;
            Tube(new(-0.42f, 0.30f, z), new(0.42f, 0.30f, z), 0.02f, FrameColor);
            Tube(new(-0.42f, 0.30f, z), new(-0.24f, 0.37f, end * 0.70f), 0.018f, FrameColor);
            Tube(new(0.42f, 0.30f, z), new(0.24f, 0.37f, end * 0.70f), 0.018f, FrameColor);
        }

        // --- Кузов-купе (поднят над гусеницами) ---
        Raylib.DrawCube(new Vector3(0, 0.50f, 0), 0.78f, 0.20f, 1.45f, BodyColor);
        Raylib.DrawCubeWires(new Vector3(0, 0.50f, 0), 0.78f, 0.20f, 1.45f, BodyEdge);
        Raylib.DrawCube(new Vector3(0, 0.612f, 0.42f), 0.60f, 0.025f, 0.55f, BodyColor);   // выпуклость капота
        DrawGreenhouse();

        // Фары, решётка, задние фонари (горят ярче при торможении)
        bool braking = MathF.Abs(car.Speed) > 0.05f &&
                       (car.Mode == MotionMode.Stopped ||
                        MathF.Abs(car.Speed) > Settings.KmhToMs(car.SetpointKmh) + 0.05f);
        var tail = braking ? new Color(255, 40, 40, 255) : new Color(140, 20, 20, 255);
        foreach (float side in new[] { -1f, 1f })
        {
            Raylib.DrawCube(new Vector3(side * 0.27f, 0.54f, 0.728f), 0.17f, 0.05f, 0.01f, new Color(235, 235, 215, 255));
            Raylib.DrawCube(new Vector3(side * 0.27f, 0.55f, -0.728f), 0.18f, 0.07f, 0.01f, tail);
            Raylib.DrawCube(new Vector3(side * 0.33f, 0.665f, 0.12f), 0.06f, 0.04f, 0.05f, BodyColor);  // зеркала
        }
        Raylib.DrawCube(new Vector3(0, 0.50f, 0.728f), 0.26f, 0.08f, 0.01f, new Color(45, 45, 50, 255));
        Raylib.DrawCube(new Vector3(0, 0.47f, -0.728f), 0.22f, 0.06f, 0.01f, Chrome);       // табличка номера

        // Ультразвуковой датчик на переднем бампере (как HC-SR04 из набора)
        Raylib.DrawCube(new Vector3(0, 0.30f, 0.835f), 0.20f, 0.08f, 0.01f, new Color(30, 90, 180, 255));
        foreach (float side in new[] { -1f, 1f })
        {
            var eye = new Vector3(side * 0.05f, 0.30f, 0.84f);
            Raylib.DrawCylinderEx(eye, eye + new Vector3(0, 0, 0.03f), 0.03f, 0.03f, 12, Chrome);
        }

        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Остеклённая часть кузова: лобовое стекло, крыша, длинное покатое заднее стекло (фастбэк).
    /// Боковины — многоугольник по профилю, стороны соединены четырёхугольниками.
    /// </summary>
    private static void DrawGreenhouse()
    {
        const float halfW = 0.29f, roofHalfW = 0.25f;
        // Профиль сбоку (z, y): низ лобового, верх лобового, конец крыши, низ заднего стекла
        var profile = new (float Z, float Y)[] { (0.22f, 0.60f), (0.00f, 0.79f), (-0.30f, 0.79f), (-0.66f, 0.60f) };

        Vector3 P(int i, float side) =>
            new(side * (profile[i].Y > 0.7f ? roofHalfW : halfW), profile[i].Y, profile[i].Z);

        // Боковые стёкла
        foreach (float side in new[] { -1f, 1f })
        {
            Raylib.DrawTriangle3D(P(0, side), P(1, side), P(2, side), GlassColor);
            Raylib.DrawTriangle3D(P(0, side), P(2, side), P(3, side), GlassColor);
        }

        // Лобовое, крыша (цвет кузова), заднее стекло
        Quad(P(0, 1), P(0, -1), P(1, -1), P(1, 1), GlassColor);
        Quad(P(1, 1), P(1, -1), P(2, -1), P(2, 1), BodyColor);
        Quad(P(2, 1), P(2, -1), P(3, -1), P(3, 1), GlassColor);

        // Стойки и рамки окон — светлые линии, чтобы был виден силуэт
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < profile.Length - 1; i++)
                Raylib.DrawLine3D(P(i, side), P(i + 1, side), BodyEdge);
        for (int i = 0; i < profile.Length; i++)
            Raylib.DrawLine3D(P(i, 1), P(i, -1), BodyEdge);
        // Центральная стойка
        foreach (float side in new[] { -1f, 1f })
            Raylib.DrawLine3D(new(side * halfW, 0.60f, -0.12f), new(side * roofHalfW, 0.79f, -0.12f), BodyEdge);
    }

    /// <summary>
    /// Гусеница: звёздочка спереди, ленивец сзади, опорные катки с пружинами
    /// и лента из траков, которые бегут по контуру со скоростью движения гусеницы.
    /// </summary>
    private static void DrawTrack(float x, float travelled, float wheelAngle)
    {
        var half = new Vector3(TrackBeltWidth / 2f - 0.01f, 0, 0);
        float outward = Math.Sign(x);

        // Звёздочка и ленивец (с метками, по которым видно вращение)
        foreach (float z in new[] { TrackHalfBase, -TrackHalfBase })
        {
            var c = new Vector3(x, TrackCenterY, z);
            Raylib.DrawCylinderEx(c - half, c + half, TrackR - 0.02f, TrackR - 0.02f, 20, RollerColor);
            var face = c + new Vector3(outward * TrackBeltWidth / 2f, 0, 0);
            for (int i = 0; i < 6; i++)
            {
                float a = wheelAngle + i * MathF.PI / 3f;
                Raylib.DrawLine3D(face, face + new Vector3(0, MathF.Cos(a), MathF.Sin(a)) * (TrackR - 0.03f), Chrome);
            }
        }

        // Опорные катки и пружины подвески над ними
        for (int i = 0; i < 6; i++)
        {
            float z = -0.40f + i * 0.16f;
            var c = new Vector3(x, 0.105f, z);
            Raylib.DrawCylinderEx(c - half, c + half, 0.075f, 0.075f, 16, RollerColor);
            if (i % 2 == 0)
            {
                var springBase = new Vector3(x - outward * 0.12f, 0.12f, z + 0.08f);
                Raylib.DrawCylinderWiresEx(springBase, springBase + new Vector3(0, 0.24f, 0), 0.03f, 0.03f, 6, SpringColor);
                Tube(springBase, springBase + new Vector3(0, 0.24f, 0), 0.008f, FrameColor);
            }
        }

        // Траки. Параметр s — положение вдоль контура ленты:
        // верхняя ветвь (назад→вперёд), передняя дуга, нижняя ветвь (вперёд→назад), задняя дуга.
        // При движении вперёд верх ленты бежит вперёд, низ — назад, относительно земли низ стоит.
        float spacing = TrackPerimeter / TrackLinks;
        for (int i = 0; i < TrackLinks; i++)
        {
            float s = Mod(i * spacing + travelled, TrackPerimeter);
            var (y, z, pitchDeg) = TrackPoint(s);

            Rlgl.PushMatrix();
            Rlgl.Translatef(x, y, z);
            Rlgl.Rotatef(pitchDeg, 1f, 0f, 0f);
            Raylib.DrawCube(Vector3.Zero, TrackBeltWidth, 0.035f, spacing * 0.82f, LinkColor);
            Raylib.DrawCube(new Vector3(0, 0.02f, 0), TrackBeltWidth * 0.95f, 0.012f, 0.025f, new Color(28, 28, 30, 255)); // грунтозацеп
            Rlgl.PopMatrix();
        }
    }

    /// <summary>Точка на контуре гусеницы: высота, продольная координата и наклон трака.</summary>
    private static (float Y, float Z, float PitchDeg) TrackPoint(float s)
    {
        float L = TrackHalfBase, R = TrackR, H = TrackCenterY;
        float straight = 2f * L, arc = MathF.PI * R;

        if (s < straight)                                   // верхняя ветвь
            return (H + R, -L + s, 0f);
        s -= straight;
        if (s < arc)                                        // передняя дуга (сверху вниз)
        {
            float a = s / R;
            return (H + R * MathF.Cos(a), L + R * MathF.Sin(a), a * 180f / MathF.PI);
        }
        s -= arc;
        if (s < straight)                                   // нижняя ветвь
            return (H - R, L - s, 180f);
        s -= straight;
        {                                                   // задняя дуга (снизу вверх)
            float a = s / R;
            return (H - R * MathF.Cos(a), -L - R * MathF.Sin(a), 180f + a * 180f / MathF.PI);
        }
    }

    private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Raylib.DrawTriangle3D(a, b, c, color);
        Raylib.DrawTriangle3D(a, c, d, color);
    }

    private static void Tube(Vector3 a, Vector3 b, float radius, Color color)
        => Raylib.DrawCylinderEx(a, b, radius, radius, 8, color);

    private static float Mod(float a, float m) => ((a % m) + m) % m;
}
