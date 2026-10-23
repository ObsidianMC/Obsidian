namespace Obsidian.Entities.AI;

internal static class EntityMovement
{
    // The shapes a collision is checked against, reused on the thread: entities move many times a tick.
    [ThreadStatic] private static List<BoundingBox>? collisionShapes;

    // A mob falls by its gravity attribute, like vanilla's LivingEntity.getGravity.
    internal static VectorD Move(Mob mob) => Move(
        mob,
        mob.Terrain,
        mob.MoveControl.Acceleration,
        mob.HasAttribute("minecraft:gravity") ? mob.GetAttributeValue("minecraft:gravity") : 0.08f);

    internal static VectorD Move(Entity mob, MobTerrain terrain, VectorD acceleration, float gravity = 0.08f, float airFriction = 0.91f)
    {
        var velocity = mob.Motion + acceleration;
        if (mob is Living living && living.ActivePotionEffects.TryGetValue((int)PotionEffect.Levitation - 1, out var levitation))
        {
            velocity.Y += (0.05f * (levitation.EffectData.Amplifier + 1) - velocity.Y) * 0.2f;
            gravity = 0;
        }
        var fluidBounds = mob.Dimension.CreateBBFromPosition(mob.Position);

        // A mob's own terrain has its fluid contact for this tick already.
        var cached = mob is Mob withTerrain && ReferenceEquals(withTerrain.Terrain, terrain) ? withTerrain : null;
        var inWater = cached?.InWater ?? terrain.ContainsFluid(fluidBounds, water: true);
        var inLava = cached?.InLava ?? terrain.ContainsFluid(fluidBounds, water: false);
        if (mob is Mob && mob is not (Fish or Squid or Dolphin or Axolotl or Frog or Turtle or Nautilus or EnderDragon or Drowned { Swimming: true }))
        {
            if (inWater) velocity += terrain.FluidPush(fluidBounds, water: true, velocity);
            if (inLava) velocity += terrain.FluidPush(fluidBounds, water: false, velocity);
        }
        var grounded = mob.MovementFlags.HasFlag(MovementFlags.OnGround);
        if (mob is Mob jumpingMob && jumpingMob.JumpControl.Consume())
        {
            if (inWater || inLava)
                velocity.Y += 0.04f;
            else if (grounded && jumpingMob.TryJump())
                velocity.Y = jumpingMob.JumpPower;
        }

        var bounds = mob.Dimension.CreateBBFromPosition(mob.Position);
        var stepHeight = Math.Clamp(mob.GetAttributeValue("minecraft:generic.step_height"), 0, 10);
        var displacement = Collide(terrain, bounds, velocity, mob is Mob ? stepHeight : 0, grounded);
        var horizontalCollision = Math.Abs(displacement.X - velocity.X) > 0.00001f ||
            Math.Abs(displacement.Z - velocity.Z) > 0.00001f;

        var onGround = velocity.Y < 0 && Math.Abs(displacement.Y - velocity.Y) > 0.00001f;
        if (!onGround && Math.Abs(velocity.Y) < 0.00001f)
            onGround = !terrain.IsFree(bounds.OffsetBy(displacement + new VectorD(0, -0.001f, 0)));

        mob.MovementFlags = (onGround ? MovementFlags.OnGround : MovementFlags.None) |
            (horizontalCollision ? MovementFlags.HorizontalCollision : MovementFlags.None);

        if (Math.Abs(displacement.X - velocity.X) > 0.00001f)
            velocity.X = 0;
        if (Math.Abs(displacement.Z - velocity.Z) > 0.00001f)
            velocity.Z = 0;
        if (Math.Abs(displacement.Y - velocity.Y) > 0.00001f)
            velocity.Y = 0;

        if (!mob.NoGravity)
            velocity.Y -= inWater || inLava ? 0.02f : gravity;

        var friction = inWater ? 0.8f : inLava ? 0.5f : grounded ? terrain.GetFriction(mob.Position) * airFriction : airFriction;
        mob.Motion = new VectorD(velocity.X * friction, velocity.Y * (inWater ? 0.8f : inLava ? 0.5f : 0.98f), velocity.Z * friction);
        if (mob is Mob && (inWater || inLava) && horizontalCollision)
        {
            // Vanilla's fluid collision boost allows mobs to climb onto a bank.
            var exit = bounds.OffsetBy(displacement + new VectorD(mob.Motion.X,
                mob.Motion.Y + 0.6f - displacement.Y, mob.Motion.Z));
            if (terrain.IsFree(exit) && terrain.IsFreeOfFluid(exit))
                mob.Motion = new VectorD(mob.Motion.X, 0.3f, mob.Motion.Z);
        }
        return mob.Position + displacement;
    }

    /// <summary>
    /// How far <paramref name="bounds"/> moves of <paramref name="velocity"/> before blocks stop it, stepping up
    /// blocks up to <paramref name="stepHeight"/> high when it stands on the ground or lands, if that gets it further.
    /// </summary>
    internal static VectorD Collide(
        MobTerrain terrain,
        BoundingBox bounds,
        VectorD velocity,
        float stepHeight,
        bool grounded)
    {
        var swept = new BoundingBox(VectorD.Min(bounds.Min, bounds.Min + velocity),
            VectorD.Max(bounds.Max, bounds.Max + velocity) + new VectorD(0, stepHeight, 0));
        var shapes = collisionShapes ??= [];
        shapes.Clear();
        terrain.CollectCollisions(swept, shapes);
        var displacement = Clip(bounds, velocity, shapes);
        var horizontalCollision = Math.Abs(displacement.X - velocity.X) > 0.00001f ||
            Math.Abs(displacement.Z - velocity.Z) > 0.00001f;

        if (stepHeight > 0 && horizontalCollision && (grounded || velocity.Y < 0 && displacement.Y != velocity.Y))
        {
            var rise = Clip(bounds, new VectorD(0, stepHeight, 0), shapes);
            var step = Clip(bounds.OffsetBy(rise), new VectorD(velocity.X, 0, velocity.Z), shapes);
            var descent = Clip(bounds.OffsetBy(rise + step), new VectorD(0, velocity.Y - rise.Y, 0), shapes);
            var stepped = rise + step + descent;
            if (stepped.X * stepped.X + stepped.Z * stepped.Z
                > displacement.X * displacement.X + displacement.Z * displacement.Z)
            {
                displacement = stepped;
            }
        }

        return displacement;
    }

    internal static VectorD Clip(BoundingBox bounds, VectorD desired, IReadOnlyList<BoundingBox> shapes)
    {
        var y = desired.Y;
        foreach (var shape in shapes)
        {
            if (bounds.Max.X > shape.Min.X && bounds.Min.X < shape.Max.X && bounds.Max.Z > shape.Min.Z && bounds.Min.Z < shape.Max.Z)
                y = ClipAxis(bounds.Min.Y, bounds.Max.Y, shape.Min.Y, shape.Max.Y, y);
        }
        bounds = bounds.OffsetBy(new VectorD(0, y, 0));

        var x = desired.X;
        foreach (var shape in shapes)
        {
            if (bounds.Max.Y > shape.Min.Y && bounds.Min.Y < shape.Max.Y && bounds.Max.Z > shape.Min.Z && bounds.Min.Z < shape.Max.Z)
                x = ClipAxis(bounds.Min.X, bounds.Max.X, shape.Min.X, shape.Max.X, x);
        }
        bounds = bounds.OffsetBy(new VectorD(x, 0, 0));

        var z = desired.Z;
        foreach (var shape in shapes)
        {
            if (bounds.Max.Y > shape.Min.Y && bounds.Min.Y < shape.Max.Y && bounds.Max.X > shape.Min.X && bounds.Min.X < shape.Max.X)
                z = ClipAxis(bounds.Min.Z, bounds.Max.Z, shape.Min.Z, shape.Max.Z, z);
        }

        return new VectorD(x, y, z);
    }

    private static double ClipAxis(double min, double max, double obstacleMin, double obstacleMax, double movement)
    {
        if (movement > 0 && max <= obstacleMin)
            return Math.Min(movement, obstacleMin - max);
        if (movement < 0 && min >= obstacleMax)
            return Math.Max(movement, obstacleMax - min);
        return movement;
    }
}
