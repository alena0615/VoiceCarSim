using System.Numerics;

namespace VoiceCarSim.Control;

public enum MotionMode
{
    Stopped,   // стоит
    Forward,   // едет вперёд
    Backward,  // едет назад
    Turning,   // поворачивает на месте (после «направо» / «налево»)
}

/// <summary>
/// Логика управления машинкой: принимает команды, хранит состояние
/// и плавно двигает модель (кинематика дифференциального привода).
///
/// Этот класс не знает ни про голос, ни про графику. В дипломе его можно
/// оставить как есть и вместо 3D-модели отправлять скорости колёс на Arduino.
///
/// Система координат: Y вверх, машинка смотрит вдоль вектора (sin Yaw, 0, cos Yaw).
/// Yaw растёт при повороте налево (против часовой стрелки, если смотреть сверху).
/// </summary>
public sealed class CarController
{
    public Vector3 Position { get; private set; }
    public float Yaw { get; private set; }          // текущий угол, градусы
    public float TargetYaw { get; private set; }    // к какому углу стремимся
    public float Speed { get; private set; }        // текущая скорость, м/с (минус — назад)
    public float SetpointKmh { get; private set; }  // заданная скорость, км/ч (модуль)
    public MotionMode Mode { get; private set; } = MotionMode.Stopped;

    public float LeftWheelAngle { get; private set; }   // радианы, угол ведущего колеса левой гусеницы
    public float RightWheelAngle { get; private set; }  // то же для правой

    /// <summary>Сколько прошла лента левой / правой гусеницы, м (для анимации траков).</summary>
    public float LeftTrackDistance => LeftWheelAngle * Settings.WheelRadius;
    public float RightTrackDistance => RightWheelAngle * Settings.WheelRadius;
    public float YawRate { get; private set; }          // град/с, фактическая скорость поворота

    /// <summary>Курс как на компасе: 0..360, по часовой стрелке.</summary>
    public float CourseDeg => Normalize360(-Yaw);
    public float SpeedKmh => Settings.MsToKmh(Speed);
    public Vector3 Forward => DirectionFromYaw(Yaw);

    /// <summary>Сообщения для пользователя: подтверждения, аварийные остановки.</summary>
    public event Action<string>? Notify;

    /// <summary>Сколько всего проехала машинка, м.</summary>
    public float Odometer { get; private set; }

    /// <summary>Путь, пройденный за последнее торможение («стоп», «тормоз», аварийное), м.</summary>
    public float? LastBrakingDistance { get; private set; }

    private bool _emergency;  // идёт аварийное торможение

    // Замер тормозного пути
    private bool _brakeMeasuring;
    private Vector3 _brakeStart;
    private float _brakeFromKmh;

    public CarController(Vector3 startPosition, float startYaw = 0f)
    {
        Reset(startPosition, startYaw);
    }

    public void Reset(Vector3 position, float yaw = 0f)
    {
        Position = position;
        Yaw = TargetYaw = yaw;
        Speed = 0f;
        SetpointKmh = 0f;
        Mode = MotionMode.Stopped;
        _emergency = false;
        _brakeMeasuring = false;
        Odometer = 0f;
        LastBrakingDistance = null;
    }

    /// <summary>Начать замер тормозного пути (если машинка едет).</summary>
    private void StartBrakeMeasure()
    {
        if (MathF.Abs(Speed) < 0.1f) return;
        _brakeMeasuring = true;
        _brakeStart = Position;
        _brakeFromKmh = MathF.Abs(SpeedKmh);
    }

    // ------------------------------------------------------------------
    //  Обработка команд
    // ------------------------------------------------------------------

    public void Handle(Command command)
    {
        switch (command)
        {
            case Command.Stop:
                Mode = MotionMode.Stopped;
                SetpointKmh = 0f;
                TargetYaw = Yaw;            // прекратить и поворот тоже
                StartBrakeMeasure();
                Say("Стоп");
                break;

            case Command.Forward:
                if (Mode != MotionMode.Forward)
                {
                    Mode = MotionMode.Forward;
                    SetpointKmh = Settings.BaseSpeedKmh;
                }
                TargetYaw = Yaw;            // ехать туда, куда сейчас смотрим
                _emergency = false;
                _brakeMeasuring = false;
                Say($"Прямо, {SetpointKmh:0} км/ч");
                break;

            case Command.Backward:
                if (Mode != MotionMode.Backward)
                {
                    Mode = MotionMode.Backward;
                    SetpointKmh = Settings.ReverseSpeedKmh;
                }
                TargetYaw = Yaw;
                _emergency = false;
                _brakeMeasuring = false;
                Say($"Назад, {SetpointKmh:0} км/ч");
                break;

            case Command.Gas:
                _brakeMeasuring = false;
                if (Mode == MotionMode.Forward)
                {
                    float before = SetpointKmh;
                    SetpointKmh = MathF.Min(SetpointKmh + Settings.SpeedStepKmh, Settings.MaxForwardKmh);
                    Say(SetpointKmh > before
                        ? $"Газ: {SetpointKmh:0} км/ч"
                        : $"Максимальная скорость {Settings.MaxForwardKmh:0} км/ч");
                }
                else if (Mode == MotionMode.Backward)
                {
                    float before = SetpointKmh;
                    SetpointKmh = MathF.Min(SetpointKmh + Settings.SpeedStepKmh, Settings.MaxReverseKmh);
                    Say(SetpointKmh > before
                        ? $"Газ назад: {SetpointKmh:0} км/ч"
                        : $"Максимальная скорость назад {Settings.MaxReverseKmh:0} км/ч");
                }
                else
                {
                    Say("«Газ» не принят: сначала скажите «прямо» или «назад»");
                }
                break;

            case Command.Brake:
                if (Mode is MotionMode.Forward or MotionMode.Backward)
                {
                    SetpointKmh -= Settings.BrakeStepKmh;
                    StartBrakeMeasure();
                    if (SetpointKmh <= 0.01f)
                    {
                        // Дотормозили до нуля — обычная остановка
                        Mode = MotionMode.Stopped;
                        SetpointKmh = 0f;
                        TargetYaw = Yaw;
                        Say("Тормоз: остановка");
                    }
                    else
                    {
                        Say($"Тормоз: {SetpointKmh:0} км/ч");
                    }
                }
                else
                {
                    Say("«Тормоз» не нужен: машинка не едет");
                }
                break;

            case Command.Right:
            case Command.Left:
                // Поворот на месте: сначала остановиться, потом развернуться.
                float sign = command == Command.Left ? +1f : -1f;
                float from = Mode == MotionMode.Turning ? TargetYaw : Yaw;  // «направо, направо» = 180°
                Mode = MotionMode.Turning;
                SetpointKmh = 0f;
                TargetYaw = from + sign * Settings.BigTurnDeg;
                Say(command == Command.Left ? "Поворот налево" : "Поворот направо");
                break;

            case Command.Righter:
            case Command.Lefter:
                // Подруливание: продолжаем движение, курс смещается на 5°.
                // Если машинка стоит — небольшой поворот на месте.
                float s = command == Command.Lefter ? +1f : -1f;
                TargetYaw += s * Settings.SmallTurnDeg;
                Say(command == Command.Lefter
                    ? $"Левее на {Settings.SmallTurnDeg:0}°"
                    : $"Правее на {Settings.SmallTurnDeg:0}°");
                break;
        }
    }

    // ------------------------------------------------------------------
    //  Движение (вызывается каждый кадр)
    // ------------------------------------------------------------------

    /// <param name="dt">время кадра, с</param>
    /// <param name="frontDistance">расстояние до препятствия спереди (или +∞)</param>
    /// <param name="rearDistance">расстояние до препятствия сзади (или +∞)</param>
    /// <param name="collides">проверка: пересекается ли машинка с чем-то в этой точке</param>
    public void Update(float dt, float frontDistance, float rearDistance, Func<Vector3, bool> collides)
    {
        // 1. Какую скорость хотим
        float desired = Mode switch
        {
            MotionMode.Forward => Settings.KmhToMs(SetpointKmh),
            MotionMode.Backward => -Settings.KmhToMs(SetpointKmh),
            _ => 0f,
        };

        // 2. Защита от столкновения: если тормозной путь не помещается до препятствия — аварийный стоп
        CheckObstacle(desired, frontDistance, rearDistance);
        if (_emergency) desired = 0f;

        // 3. Плавно меняем скорость
        float rate;
        if (_emergency) rate = Settings.EmergencyDeceleration;
        else if (desired == 0f && Mode == MotionMode.Stopped) rate = Settings.StopDeceleration;
        else if (MathF.Abs(desired) > MathF.Abs(Speed) && MathF.Sign(desired) == MathF.Sign(Speed) || Speed == 0f)
            rate = Settings.Acceleration;
        else rate = Settings.Deceleration;

        Speed = MoveTowards(Speed, desired, rate * dt);
        if (_emergency && Speed == 0f) _emergency = false;
        bool brakingDone = _brakeMeasuring && Speed == desired;

        // 4. Поворот. Поворот на месте начинается только после полной остановки.
        bool almostStopped = MathF.Abs(Speed) < 0.05f;
        float yawBefore = Yaw;
        if (!(Mode == MotionMode.Turning && !almostStopped))
        {
            float turnRate = almostStopped ? Settings.TurnRateInPlace : Settings.TurnRateMoving;
            Yaw = MoveTowards(Yaw, TargetYaw, turnRate * dt);
        }
        YawRate = dt > 0 ? (Yaw - yawBefore) / dt : 0f;

        if (Mode == MotionMode.Turning && Yaw == TargetYaw && almostStopped)
        {
            Mode = MotionMode.Stopped;
            Say($"Поворот выполнен, курс {CourseDeg:0}°");
        }

        // 5. Перемещение
        Vector3 next = Position + DirectionFromYaw(Yaw) * Speed * dt;
        if (collides(next))
        {
            Speed = 0f;
            if (Mode is MotionMode.Forward or MotionMode.Backward)
            {
                Mode = MotionMode.Stopped;
                SetpointKmh = 0f;
                Say("Столкновение! Остановка");
            }
        }
        else
        {
            Odometer += Vector3.Distance(Position, next);
            Position = next;
        }

        // Тормозной путь: от команды до момента, когда скорость стала заданной
        if (brakingDone)
        {
            _brakeMeasuring = false;
            float d = Vector3.Distance(_brakeStart, Position);
            LastBrakingDistance = d;
            Say($"Тормозной путь {d:0.0} м ({_brakeFromKmh:0} → {MathF.Abs(SpeedKmh):0} км/ч)");
        }

        // 6. Гусеницы: при повороте внешняя крутится быстрее внутренней, на месте — в разные стороны
        float omega = YawRate * MathF.PI / 180f;                 // рад/с, плюс — налево
        float vLeft = Speed - omega * Settings.TrackWidth / 2f;
        float vRight = Speed + omega * Settings.TrackWidth / 2f;
        LeftWheelAngle += vLeft / Settings.WheelRadius * dt;
        RightWheelAngle += vRight / Settings.WheelRadius * dt;
    }

    private void CheckObstacle(float desired, float front, float rear)
    {
        if (_emergency) return;

        bool goingForward = Speed > 0.01f || desired > 0f;
        bool goingBackward = Speed < -0.01f || desired < 0f;
        float brakingDistance = Speed * Speed / (2f * Settings.EmergencyDeceleration);
        float limit = brakingDistance + Settings.SafetyMargin;

        if ((goingForward && front < limit) || (goingBackward && rear < limit))
        {
            _emergency = true;
            Mode = MotionMode.Stopped;
            SetpointKmh = 0f;
            TargetYaw = Yaw;
            StartBrakeMeasure();
            Say(goingForward
                ? $"Препятствие спереди ({front:0.0} м)! Аварийная остановка"
                : $"Препятствие сзади ({rear:0.0} м)! Аварийная остановка");
        }
    }

    // ------------------------------------------------------------------

    public static Vector3 DirectionFromYaw(float yawDeg)
    {
        float r = yawDeg * MathF.PI / 180f;
        return new Vector3(MathF.Sin(r), 0f, MathF.Cos(r));
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta) return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    private static float Normalize360(float deg)
    {
        deg = ((deg % 360f) + 360f) % 360f;
        return deg >= 359.5f ? 0f : deg;
    }

    private void Say(string message) => Notify?.Invoke(message);
}
