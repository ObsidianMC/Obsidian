namespace Obsidian.Entities.AI;

public class MoveControl(Mob mob)
{
    private VectorF? wantedPosition;
    private float speedModifier;
    private VectorF? strafe;
    internal VectorF Acceleration { get; private set; }
    internal bool IsStrafing => strafe != null;

    public void MoveTo(VectorF position, float speed)
    {
        wantedPosition = position;
        speedModifier = speed;
    }

    public void Stop()
    {
        wantedPosition = null;
        strafe = null;
        Acceleration = VectorF.Zero;
    }

    internal void Strafe(float forward, float sideways) => strafe = new VectorF(sideways, 0, forward);

    internal void Ride(float speed)
    {
        wantedPosition = null;
        var yaw = mob.Yaw.Degrees * MathF.PI / 180;
        var input = new VectorF(-MathF.Sin(yaw), 0, MathF.Cos(yaw));
        var acceleration = mob.InWater ? 0.02f : mob.MovementFlags.HasFlag(MovementFlags.OnGround) ? speed : 0.02f;
        Acceleration = input * (acceleration * 0.98f);
    }

    internal virtual void Tick()
    {
        Acceleration = VectorF.Zero;
        if (strafe is VectorF input)
        {
            strafe = null;
            var strafeYaw = mob.Yaw.Degrees * MathF.PI / 180;
            var movement = new VectorF(input.X * MathF.Cos(strafeYaw) - input.Z * MathF.Sin(strafeYaw), 0,
                input.Z * MathF.Cos(strafeYaw) + input.X * MathF.Sin(strafeYaw));
            var next = mob.Position + movement;
            if (!mob.Terrain.IsFree(mob.Dimension.CreateBBFromPosition(next)))
                movement = new VectorF(-MathF.Sin(strafeYaw), 0, MathF.Cos(strafeYaw));
            Acceleration = movement * (mob.MovementSpeed * 0.25f * 0.98f);
            return;
        }
        if (wantedPosition is not VectorF target)
            return;
        wantedPosition = null;

        var delta = target - mob.Position;
        var horizontalDistance = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (horizontalDistance < 0.05f)
            return;

        var wantedYaw = MathF.Atan2(delta.Z, delta.X) * 180 / MathF.PI - 90;
        mob.Yaw = LookControl.RotateTowards(mob.Yaw.Degrees, wantedYaw, 90);
        var speed = mob.MovementSpeed * speedModifier;
        var friction = mob.Terrain.GetFriction(mob.Position);
        var acceleration = mob.InWater || mob.InLava ? 0.02f : mob.MovementFlags.HasFlag(MovementFlags.OnGround) ? speed * 0.21600002f / (friction * friction * friction) : 0.02f;
        var yaw = mob.Yaw.Degrees * MathF.PI / 180;
        Acceleration = new VectorF(-MathF.Sin(yaw), 0, MathF.Cos(yaw)) * (acceleration * speed * 0.98f);
        if (delta.Y > 0.6f && horizontalDistance < MathF.Max(1, mob.Dimension.Width))
            mob.JumpControl.Jump();
    }
}

public sealed class JumpControl
{
    private bool requested;
    public void Jump() => requested = true;

    internal bool Consume()
    {
        var result = requested;
        requested = false;
        return result;
    }
}

public sealed class LookControl(Mob mob)
{
    private VectorF? wantedPosition;
    private float yawSpeed = 10;
    private float pitchSpeed = 40;
    internal Angle HeadYaw { get; private set; }

    public void LookAt(VectorF position, float maxYawChange = 10, float maxPitchChange = 40)
    {
        wantedPosition = position;
        yawSpeed = maxYawChange;
        pitchSpeed = maxPitchChange;
    }

    public void LookAt(IEntity entity) => LookAt(entity.Position + new VectorF(0,
        entity is Mob other ? other.EyeHeight : entity.Dimension.Height * 0.85f, 0));

    internal void Tick()
    {
        if (wantedPosition is VectorF target)
        {
            var difference = target - mob.EyePosition;
            if (difference.MagnitudeSquared() > 0.000001f)
            {
                var yaw = MathF.Atan2(difference.Z, difference.X) * 180 / MathF.PI - 90;
                var pitch = -MathF.Atan2(difference.Y, MathF.Sqrt(difference.X * difference.X + difference.Z * difference.Z)) * 180 / MathF.PI;
                HeadYaw = RotateTowards(HeadYaw.Degrees, yaw, yawSpeed);
                mob.Pitch = RotateTowards(mob.Pitch.Degrees, pitch, pitchSpeed);
            }
            wantedPosition = null;
        }
        else
        {
            HeadYaw = RotateTowards(HeadYaw.Degrees, mob.Yaw.Degrees, 10);
            mob.Pitch = RotateTowards(mob.Pitch.Degrees, 0, 10);
        }

        // Keep the head within the body's range while allowing idle head movement.
        var offset = WrapDegrees(HeadYaw.Degrees - mob.Yaw.Degrees);
        if (MathF.Abs(offset) > 75)
            mob.Yaw = HeadYaw.Degrees - MathF.CopySign(75, offset);
    }

    internal static float RotateTowards(float current, float target, float maximumChange) =>
        current + Math.Clamp(WrapDegrees(target - current), -maximumChange, maximumChange);

    internal static float WrapDegrees(float degrees) => (degrees % 360 + 540) % 360 - 180;
}
