namespace Obsidian.Entities.AI;

internal sealed class VolumeMoveControl(Mob mob) : MoveControl(mob)
{
    private VectorF? destination;
    private float speed;
    public override void MoveTo(VectorF position, float modifier)
    {
        destination = position;
        speed = modifier;
        base.MoveTo(position, modifier);
    }
    public override void Stop() { destination = null; base.Stop(); }
    internal override void Tick()
    {
        if (!mob.FlyingNavigation && !mob.InWater)
        {
            destination = null;
            base.Tick();
            return;
        }
        Acceleration = VectorF.Zero;
        if (destination is not VectorF target)
            return;
        destination = null;
        var delta = target - mob.Position;
        if (delta.Magnitude < 0.05f)
            return;
        mob.Yaw = LookControl.RotateTowards(mob.Yaw.Degrees, MathF.Atan2(-delta.X, delta.Z) * 180 / MathF.PI, 30);
        Acceleration = delta / delta.Magnitude * (mob.MovementSpeed * speed * (mob.InWater ? 0.02f : 0.05f));
    }
}

internal static class VolumeMovement
{
    private static readonly VectorF[] directions = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
    internal static bool UsesVolume(Mob mob) => mob.FlyingNavigation || mob.SwimmingNavigation && mob.InWater;

    internal static bool CanOccupy(Mob mob, VectorF point) =>
        mob.Terrain.IsFree(mob.Dimension.CreateBBFromPosition(point)) &&
        (mob.FlyingNavigation ? mob.Terrain.GetBlock((Vector)point.Floor()) is { IsLiquid: false } :
            mob.Terrain.GetBlock((Vector)point.Floor())?.Material == Material.Water);

    internal static IEnumerable<VectorF> Neighbors(Mob mob, VectorF point)
    {
        var bounds = mob.Dimension.CreateBBFromPosition(point);
        foreach (var offset in directions)
        {
            var next = point + offset;
            var nextBounds = mob.Dimension.CreateBBFromPosition(next);
            if (CanOccupy(mob, next) && mob.Terrain.IsFree(new BoundingBox(VectorF.Min(bounds.Min, nextBounds.Min), VectorF.Max(bounds.Max, nextBounds.Max))))
                yield return next;
        }
    }

    internal static VectorF Travel(Mob mob, bool weightless)
    {
        var gravity = mob.NoGravity;
        mob.NoGravity |= weightless;
        if (weightless && !mob.InWater && !mob.InLava)
            mob.Motion = new VectorF(mob.Motion.X, mob.Motion.Y * (0.91f / 0.98f), mob.Motion.Z);
        var next = EntityMovement.Move(mob);
        mob.NoGravity = gravity;
        return next;
    }
}
