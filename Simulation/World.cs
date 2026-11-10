using System.Numerics;
using Raylib_cs;

namespace VoiceCarSim.Simulation;

/// <summary>Прямоугольное препятствие (здесь — только ограждение по краю карты).</summary>
public readonly record struct Obstacle(Vector2 Center, Vector2 Size, float Height, Color Color)
{
    public Vector2 Min => Center - Size / 2f;
    public Vector2 Max => Center + Size / 2f;
}

/// <summary>Четырёхугольник на полу (кусок асфальта или линии разметки).</summary>
public readonly record struct FloorQuad(Vector2 A, Vector2 B, Vector2 C, Vector2 D, Color Color);

/// <summary>
/// Карта 100×100 м: трава и одна кольцевая дорога через всю карту.
///
/// Трасса (по ходу движения от старта):
///   длинная прямая ~80 м (разгон, «газ», «тормоз», «стоп») →
///   поворот налево → поворот налево → поворот направо →
///   плавный изгиб влево на 15° (три раза «левее») → изгиб вправо на 15° («правее» ×3) →
///   налево → налево → направо → налево → налево → снова старт.
///
/// Плюс имитация ультразвуковых датчиков (лучи из передней и задней части машинки).
/// Работаем в плоскости XZ: Vector2(x, z).
/// </summary>
public sealed class World
{
    public const float RoomSize = 100f;
    public const float RoadHalfWidth = 3f;        // дорога шириной 6 м

    /// <summary>Где машинка появляется: в начале длинной прямой, носом вдоль дороги.</summary>
    public static readonly Vector3 StartPosition = new(-40f, 0f, -37f);

    public readonly List<Obstacle> Obstacles = new();
    public readonly List<FloorQuad> FloorQuads = new();

    /// <summary>Осевая линия дороги: точки через ~0,5 м, замкнутый контур.</summary>
    public readonly List<Vector2> RoadCenter = new();

    // Три луча на датчик: прямо и ±12°, как конус ультразвукового датчика
    private static readonly float[] BeamOffsets = { -12f, 0f, 12f };

    // Узловые точки трассы (углы). Между ними — прямые, углы скругляются.
    private static readonly Vector2[] RouteCorners =
    {
        new(-40, -42),
        new(-40, 40),     // конец длинной прямой → налево
        new(-15, 40),     // налево
        new(-15, 10),     // направо
        new(5, 10),       // изгиб влево на 15°
        new(25, 4.64f),   // изгиб вправо на 15°
        new(40, 4.64f),   // налево
        new(40, -30),     // налево
        new(10, -30),     // направо
        new(10, -42),     // налево
                          // и налево — к первой точке
    };

    public World()
    {
        BuildFence();
        BuildRoad();
    }

    private void BuildFence()
    {
        float h = RoomSize / 2f;
        var fence = new Color(150, 120, 90, 255);
        Obstacles.Add(new(new(0, h), new(RoomSize, 0.3f), 0.8f, fence));
        Obstacles.Add(new(new(0, -h), new(RoomSize, 0.3f), 0.8f, fence));
        Obstacles.Add(new(new(h, 0), new(0.3f, RoomSize), 0.8f, fence));
        Obstacles.Add(new(new(-h, 0), new(0.3f, RoomSize), 0.8f, fence));
    }

    // ------------------------------------------------------------------
    //  Дорога
    // ------------------------------------------------------------------

    private void BuildRoad()
    {
        BuildCenterline();

        var asphalt = new Color(80, 82, 88, 255);
        var white = new Color(240, 240, 240, 255);
        var yellow = new Color(240, 200, 40, 255);
        int n = RoadCenter.Count;

        // Нормали в точках осевой линии (влево по ходу движения)
        var normals = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var dir = Vector2.Normalize(RoadCenter[(i + 1) % n] - RoadCenter[(i - 1 + n) % n]);
            normals[i] = new Vector2(dir.Y, -dir.X);
        }

        // Полосы вдоль дороги: асфальт, края, прерывистая ось.
        // Порядок важен: следующий слой рисуется поверх предыдущего.
        AddStrip(normals, -RoadHalfWidth, RoadHalfWidth, asphalt, _ => true);
        AddStrip(normals, RoadHalfWidth - 0.35f, RoadHalfWidth - 0.2f, white, _ => true);
        AddStrip(normals, -RoadHalfWidth + 0.2f, -RoadHalfWidth + 0.35f, white, _ => true);
        AddStrip(normals, -0.08f, 0.08f, white, dist => dist % 6f < 3f);  // штрих 3 м, пропуск 3 м

        // Линия старта и отметки каждые 10 м по краям дороги
        int startIndex = ClosestCenterIndex(new Vector2(StartPosition.X, StartPosition.Z));
        float travelled = 0f;
        float nextMark = 0f;
        for (int k = 0; k < n; k++)
        {
            int i = (startIndex + k) % n;
            if (travelled >= nextMark)
            {
                bool isStart = k == 0;
                var c = RoadCenter[i];
                var dir = new Vector2(-normals[i].Y, normals[i].X);
                if (isStart)
                {
                    AddRect(c, dir, normals[i], 0.4f, RoadHalfWidth * 2f, yellow);
                }
                else
                {
                    AddRect(c + normals[i] * (RoadHalfWidth - 1.0f), dir, normals[i], 0.15f, 1.2f, white);
                    AddRect(c - normals[i] * (RoadHalfWidth - 1.0f), dir, normals[i], 0.15f, 1.2f, white);
                }
                nextMark += 10f;
            }
            travelled += Vector2.Distance(RoadCenter[i], RoadCenter[(i + 1) % n]);
        }
    }

    /// <summary>
    /// Строит плотную осевую линию: прямые между углами и дуги на углах.
    /// Повороты на 90° скругляются радиусом 3 м, плавные изгибы — 12 м.
    /// </summary>
    private void BuildCenterline()
    {
        int m = RouteCorners.Length;
        Vector2? firstArcStart = null;
        Vector2 cursor = default;

        for (int i = 0; i < m; i++)
        {
            var prev = RouteCorners[(i - 1 + m) % m];
            var p = RouteCorners[i];
            var next = RouteCorners[(i + 1) % m];
            var dIn = Vector2.Normalize(p - prev);
            var dOut = Vector2.Normalize(next - p);

            float turn = MathF.Acos(Math.Clamp(Vector2.Dot(dIn, dOut), -1f, 1f)); // угол поворота, рад
            float radius = turn > 0.8f ? 3f : 12f;
            float t = radius * MathF.Tan(turn / 2f);
            var arcStart = p - dIn * t;
            var arcEnd = p + dOut * t;

            if (firstArcStart == null) firstArcStart = arcStart;
            else AddLine(cursor, arcStart);

            // Дуга: центр окружности сбоку от угла
            float cross = dIn.X * dOut.Y - dIn.Y * dOut.X;
            var side = cross > 0 ? new Vector2(-dIn.Y, dIn.X) : new Vector2(dIn.Y, -dIn.X);
            var center = arcStart + side * radius;
            float a0 = MathF.Atan2(arcStart.Y - center.Y, arcStart.X - center.X);
            float a1 = MathF.Atan2(arcEnd.Y - center.Y, arcEnd.X - center.X);
            float da = a1 - a0;
            while (da > MathF.PI) da -= 2f * MathF.PI;
            while (da < -MathF.PI) da += 2f * MathF.PI;
            int steps = Math.Max(2, (int)(MathF.Abs(da) * radius / 0.5f));
            for (int s = 0; s < steps; s++)
            {
                float a = a0 + da * s / steps;
                RoadCenter.Add(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
            }
            cursor = arcEnd;
        }
        AddLine(cursor, firstArcStart!.Value);
    }

    private void AddLine(Vector2 from, Vector2 to)
    {
        float len = Vector2.Distance(from, to);
        int steps = Math.Max(1, (int)(len / 0.5f));
        for (int s = 0; s < steps; s++)
            RoadCenter.Add(Vector2.Lerp(from, to, (float)s / steps));
    }

    /// <summary>Полоса вдоль всей дороги между смещениями offA и offB от оси.</summary>
    private void AddStrip(Vector2[] normals, float offA, float offB, Color color, Func<float, bool> visibleAt)
    {
        int n = RoadCenter.Count;
        float dist = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (visibleAt(dist))
            {
                FloorQuads.Add(new FloorQuad(
                    RoadCenter[i] + normals[i] * offA,
                    RoadCenter[j] + normals[j] * offA,
                    RoadCenter[j] + normals[j] * offB,
                    RoadCenter[i] + normals[i] * offB,
                    color));
            }
            dist += Vector2.Distance(RoadCenter[i], RoadCenter[j]);
        }
    }

    private void AddRect(Vector2 center, Vector2 along, Vector2 across, float lengthAlong, float widthAcross, Color color)
    {
        var a = along * lengthAlong / 2f;
        var b = across * widthAcross / 2f;
        FloorQuads.Add(new FloorQuad(center - a - b, center + a - b, center + a + b, center - a + b, color));
    }

    private int ClosestCenterIndex(Vector2 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < RoadCenter.Count; i++)
        {
            float d = Vector2.DistanceSquared(p, RoadCenter[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Расстояние от точки до оси дороги, м.</summary>
    public float DistanceToRoadCenter(Vector3 position)
    {
        var p = new Vector2(position.X, position.Z);
        float best = float.MaxValue;
        int n = RoadCenter.Count;
        for (int i = 0; i < n; i++)
        {
            var a = RoadCenter[i];
            var b = RoadCenter[(i + 1) % n];
            var ab = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            float d = Vector2.Distance(p, a + ab * t);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>Находится ли центр машинки на дороге.</summary>
    public bool IsOnRoad(Vector3 position) => DistanceToRoadCenter(position) <= RoadHalfWidth;

    // ------------------------------------------------------------------
    //  Столкновения и датчики
    // ------------------------------------------------------------------

    /// <summary>Пересекается ли круг радиуса r с каким-нибудь препятствием.</summary>
    public bool Collides(Vector3 position, float radius)
    {
        var p = new Vector2(position.X, position.Z);
        foreach (var o in Obstacles)
        {
            var closest = Vector2.Clamp(p, o.Min, o.Max);
            if (Vector2.DistanceSquared(p, closest) < radius * radius) return true;
        }
        return false;
    }

    /// <summary>
    /// Показание датчика: расстояние от точки <paramref name="origin"/> в направлении
    /// <paramref name="yawDeg"/> до ближайшего препятствия. Если ничего нет — +∞.
    /// </summary>
    public float Sense(Vector3 origin, float yawDeg)
    {
        float best = float.PositiveInfinity;
        var o2 = new Vector2(origin.X, origin.Z);
        foreach (float offset in BeamOffsets)
        {
            float r = (yawDeg + offset) * MathF.PI / 180f;
            var dir = new Vector2(MathF.Sin(r), MathF.Cos(r));
            foreach (var ob in Obstacles)
            {
                float t = RayBox(o2, dir, ob.Min, ob.Max);
                if (t < best) best = t;
            }
        }
        return best <= Settings.SensorRange ? best : float.PositiveInfinity;
    }

    /// <summary>Пересечение луча с прямоугольником (метод «слэбов»).</summary>
    private static float RayBox(Vector2 origin, Vector2 dir, Vector2 min, Vector2 max)
    {
        float tMin = 0f, tMax = float.PositiveInfinity;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = axis == 0 ? origin.X : origin.Y;
            float d = axis == 0 ? dir.X : dir.Y;
            float lo = axis == 0 ? min.X : min.Y;
            float hi = axis == 0 ? max.X : max.Y;

            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < lo || o > hi) return float.PositiveInfinity;
                continue;
            }
            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) return float.PositiveInfinity;
        }
        return tMin;
    }
}
