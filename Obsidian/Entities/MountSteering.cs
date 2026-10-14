namespace Obsidian.Entities;

/// <summary>
/// How a rider's movement keys push a plugin's mob each tick, shared by the server's steering
/// (<see cref="PluginMob"/>) and the Obsidian client's prediction of the mob, so that both push it alike from the same
/// keys, rotation and attributes. The push is added to the mob's motion before it moves.
/// </summary>
/// <remarks>
/// Obsidian's own model, after vanilla's ridden horses and happy ghasts rather than a copy of them: the client sends
/// the positions it predicts with it, and the server checks them like vanilla checks a vehicle's.
/// </remarks>
internal static class MountSteering
{
    /// <summary>
    /// A walking mount's push, like a horse's: ahead at its <c>minecraft:movement_speed</c>, a quarter of that
    /// backwards and half of it sideways, slowed to a fiftieth of a block per tick in the air and in fluids.
    /// </summary>
    /// <param name="input">The rider's keys.</param>
    /// <param name="yaw">The mount's yaw, in degrees, which follows the rider's.</param>
    /// <param name="speed">The mount's movement speed.</param>
    /// <param name="onGround">Whether the mount stands on the ground.</param>
    /// <param name="inFluid">Whether the mount is in water or lava.</param>
    public static VectorD Walk(PlayerInput input, float yaw, double speed, bool onGround, bool inFluid)
    {
        var ahead = Axis(input, PlayerInput.Forward, PlayerInput.Backward);
        if (ahead < 0)
            ahead *= 0.25f;

        var sideways = Axis(input, PlayerInput.Left, PlayerInput.Right) * 0.5f;
        var radians = yaw * MathF.PI / 180;
        var push = new VectorD(
            sideways * MathF.Cos(radians) - ahead * MathF.Sin(radians),
            0,
            ahead * MathF.Cos(radians) + sideways * MathF.Sin(radians));

        if (push.MagnitudeSquared() > 1)
            push /= push.Magnitude;

        var acceleration = inFluid || !onGround ? 0.02 : speed;

        return push * (acceleration * 0.98f);
    }

    /// <summary>
    /// A flying mount's push, like a happy ghast's: towards where the rider looks (half as fast backwards), strafing
    /// sideways and rising with jump, a tenth of its <c>minecraft:flying_speed</c> at most.
    /// </summary>
    /// <param name="input">The rider's keys.</param>
    /// <param name="yaw">The rider's yaw, in degrees.</param>
    /// <param name="pitch">The rider's pitch, in degrees.</param>
    /// <param name="flyingSpeed">The mount's flying speed.</param>
    public static VectorD Fly(PlayerInput input, float yaw, float pitch, double flyingSpeed)
    {
        // Opposite keys cancel before backwards is halved, as vanilla's happy ghast's ridden input does.
        var ahead = Axis(input, PlayerInput.Forward, PlayerInput.Backward);
        if (ahead < 0)
            ahead *= 0.5f;

        var sideways = Axis(input, PlayerInput.Left, PlayerInput.Right);
        var lift = input.HasFlag(PlayerInput.Jump) ? 0.5 : 0;

        var (sinYaw, cosYaw) = MathF.SinCos(yaw * MathF.PI / 180);
        var (sinPitch, cosPitch) = MathF.SinCos(pitch * MathF.PI / 180);
        var look = new VectorD(-cosPitch * sinYaw, -sinPitch, cosPitch * cosYaw);

        var direction = look * ahead + new VectorD(cosYaw * sideways, lift, sinYaw * sideways);
        if (direction.MagnitudeSquared() > 1)
            direction /= direction.Magnitude;

        return direction * (flyingSpeed * 0.1);
    }

    // 1 for the first key, -1 for the second, 0 for both or neither.
    private static float Axis(PlayerInput input, PlayerInput positive, PlayerInput negative) =>
        (input.HasFlag(positive) ? 1f : 0) - (input.HasFlag(negative) ? 1f : 0);
}
