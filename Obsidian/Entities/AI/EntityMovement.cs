namespace Obsidian.Entities.AI;

internal static class EntityMovement
{
    internal static VectorF Move(Mob mob) => Move(mob, mob.Terrain, mob.MoveControl.Acceleration);

    internal static VectorF Move(Entity mob, MobTerrain terrain, VectorF acceleration, float gravity = 0.08f, float airFriction = 0.91f)
    {
        var velocity = mob.Motion + acceleration;
        var feet = terrain.GetBlock((Vector)(mob.Position + new VectorF(0, 0.1f, 0)).Floor());
        var inWater = feet?.Material == Material.Water;
        var inLava = feet?.Material == Material.Lava;
        var grounded = mob.MovementFlags.HasFlag(MovementFlags.OnGround);
        if (mob is Mob jumpingMob && jumpingMob.JumpControl.Consume())
        {
            if (inWater || inLava)
                velocity.Y += 0.04f;
            else if (grounded && jumpingMob.TryJump())
                velocity.Y = 0.42f;
        }

        var bounds = mob.Dimension.CreateBBFromPosition(mob.Position);
        var swept = new BoundingBox(VectorF.Min(bounds.Min, bounds.Min + velocity),
            VectorF.Max(bounds.Max, bounds.Max + velocity) + new VectorF(0, 1, 0));
        var shapes = terrain.GetCollisions(swept).ToArray();
        var displacement = Clip(bounds, velocity, shapes);
        var horizontalCollision = MathF.Abs(displacement.X - velocity.X) > 0.00001f ||
            MathF.Abs(displacement.Z - velocity.Z) > 0.00001f;

        if (mob is Mob && horizontalCollision && (grounded || velocity.Y < 0 && displacement.Y != velocity.Y))
        {
            var rise = Clip(bounds, new VectorF(0, 0.6f, 0), shapes);
            var step = Clip(bounds.OffsetBy(rise), new VectorF(velocity.X, 0, velocity.Z), shapes);
            var descent = Clip(bounds.OffsetBy(rise + step), new VectorF(0, velocity.Y - rise.Y, 0), shapes);
            var stepped = rise + step + descent;
            if (stepped.X * stepped.X + stepped.Z * stepped.Z > displacement.X * displacement.X + displacement.Z * displacement.Z)
                displacement = stepped;
        }

        var onGround = velocity.Y < 0 && MathF.Abs(displacement.Y - velocity.Y) > 0.00001f;
        if (!onGround && MathF.Abs(velocity.Y) < 0.00001f)
            onGround = !terrain.IsFree(bounds.OffsetBy(displacement + new VectorF(0, -0.001f, 0)));

        mob.MovementFlags = (onGround ? MovementFlags.OnGround : MovementFlags.None) |
            (horizontalCollision ? MovementFlags.HorizontalCollision : MovementFlags.None);

        if (MathF.Abs(displacement.X - velocity.X) > 0.00001f)
            velocity.X = 0;
        if (MathF.Abs(displacement.Z - velocity.Z) > 0.00001f)
            velocity.Z = 0;
        if (MathF.Abs(displacement.Y - velocity.Y) > 0.00001f)
            velocity.Y = 0;

        if (!mob.NoGravity)
            velocity.Y -= inWater || inLava ? 0.02f : gravity;

        var friction = inWater ? 0.8f : inLava ? 0.5f : grounded ? terrain.GetFriction(mob.Position) * airFriction : airFriction;
        mob.Motion = new VectorF(velocity.X * friction, velocity.Y * (inWater ? 0.8f : inLava ? 0.5f : 0.98f), velocity.Z * friction);
        return mob.Position + displacement;
    }

    internal static VectorF Clip(BoundingBox bounds, VectorF desired, IReadOnlyList<BoundingBox> shapes)
    {
        var y = desired.Y;
        foreach (var shape in shapes)
        {
            if (bounds.Max.X > shape.Min.X && bounds.Min.X < shape.Max.X && bounds.Max.Z > shape.Min.Z && bounds.Min.Z < shape.Max.Z)
                y = ClipAxis(bounds.Min.Y, bounds.Max.Y, shape.Min.Y, shape.Max.Y, y);
        }
        bounds = bounds.OffsetBy(new VectorF(0, y, 0));

        var x = desired.X;
        foreach (var shape in shapes)
        {
            if (bounds.Max.Y > shape.Min.Y && bounds.Min.Y < shape.Max.Y && bounds.Max.Z > shape.Min.Z && bounds.Min.Z < shape.Max.Z)
                x = ClipAxis(bounds.Min.X, bounds.Max.X, shape.Min.X, shape.Max.X, x);
        }
        bounds = bounds.OffsetBy(new VectorF(x, 0, 0));

        var z = desired.Z;
        foreach (var shape in shapes)
        {
            if (bounds.Max.Y > shape.Min.Y && bounds.Min.Y < shape.Max.Y && bounds.Max.X > shape.Min.X && bounds.Min.X < shape.Max.X)
                z = ClipAxis(bounds.Min.Z, bounds.Max.Z, shape.Min.Z, shape.Max.Z, z);
        }

        return new VectorF(x, y, z);
    }

    private static float ClipAxis(float min, float max, float obstacleMin, float obstacleMax, float movement)
    {
        if (movement > 0 && max <= obstacleMin)
            return MathF.Min(movement, obstacleMin - max);
        if (movement < 0 && min >= obstacleMax)
            return MathF.Max(movement, obstacleMax - min);
        return movement;
    }
}
