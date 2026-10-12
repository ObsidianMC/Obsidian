namespace Obsidian.Entities;

public sealed partial class EnderDragon
{
    private readonly (double Height, float Yaw)[] flightHistory = new (double, float)[64];
    private int flightHistoryIndex = -1;
    private float yawVelocity;
    private VectorD flightDisplacement;
    private VectorD DragonForward => new(Math.Sin(Yaw.Degrees * Math.PI / 180), 0, -Math.Cos(Yaw.Degrees * Math.PI / 180));

    private (double Height, float Yaw) FlightSample(int age) => flightHistoryIndex < 0
        ? (Position.Y, Yaw.Degrees)
        : flightHistory[(flightHistoryIndex - age) & 63];

    private void RecordFlight()
    {
        var sample = (Position.Y, Yaw.Degrees);
        if (flightHistoryIndex < 0) Array.Fill(flightHistory, sample);
        flightHistoryIndex = (flightHistoryIndex + 1) & 63;
        flightHistory[flightHistoryIndex] = sample;
    }

    private static double WrapDegrees(double angle) => (angle % 360 + 540) % 360 - 180;

    private VectorD TailOffset(int index, VectorD forward, double pitchCos, double pitchSin)
    {
        var sample = FlightSample(12 + index * 2);
        var reference = FlightSample(5);
        var angle = (Yaw.Degrees + WrapDegrees(sample.Yaw - reference.Yaw)) * Math.PI / 180;
        var distance = (index + 1) * 2;
        return new VectorD(-(forward.X * 1.5 + Math.Sin(angle) * distance) * pitchCos,
            sample.Height - reference.Height - (distance + 1.5) * pitchSin + 1.5,
            (-forward.Z * 1.5 + Math.Cos(angle) * distance) * pitchCos);
    }

    private void SteerFlight()
    {
        var difference = destination - Position;
        var horizontal = Math.Sqrt(difference.X * difference.X + difference.Z * difference.Z);
        var maximumRise = phase switch { DragonPhase.Landing => 1.5, DragonPhase.Dying or DragonPhase.Charging => 3, _ => 0.6 };
        var rise = horizontal > 0 ? Math.Clamp(difference.Y / horizontal, -maximumRise, maximumRise) : difference.Y;
        Motion += new VectorD(0, rise * 0.01, 0);
        Yaw = (float)WrapDegrees(Yaw.Degrees);
        var angle = Yaw.Degrees * Math.PI / 180;
        var facing = new VectorD(Math.Sin(angle), Motion.Y, -Math.Cos(angle));
        facing /= facing.Magnitude;
        var direction = difference.Magnitude > 0 ? difference / difference.Magnitude : VectorD.Zero;
        var alignment = Math.Max(0, (VectorD.Dot(facing, direction) + 0.5) / 1.5);
        if (Math.Abs(difference.X) > 0.00001 || Math.Abs(difference.Z) > 0.00001)
        {
            var turn = Math.Clamp(WrapDegrees(180 - Math.Atan2(difference.X, difference.Z) * 180 / Math.PI - Yaw.Degrees), -50, 50);
            var speed = Math.Sqrt(Motion.X * Motion.X + Motion.Z * Motion.Z) + 1;
            var turnSpeed = phase == DragonPhase.Landing ? Math.Min(speed, 40) / speed : 0.7 / Math.Min(speed, 40) / speed;
            yawVelocity = yawVelocity * 0.8f + (float)(turn * turnSpeed);
            Yaw = Yaw.Degrees + yawVelocity * 0.1f;
        }
        var proximity = 2 / (difference.MagnitudeSquared() + 1);
        var thrust = 0.06 * (alignment * proximity + 1 - proximity);
        angle = Yaw.Degrees * Math.PI / 180;
        Motion += new VectorD(Math.Sin(angle) * thrust, 0, -Math.Cos(angle) * thrust);
        flightDisplacement = Motion;
        var normalized = Motion.Magnitude > 0 ? Motion / Motion.Magnitude : VectorD.Zero;
        var drag = 0.8 + 0.15 * (VectorD.Dot(normalized, facing) + 1) / 2;
        Motion = new VectorD(Motion.X * drag, Motion.Y * 0.91, Motion.Z * drag);
    }
}
