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
                velocity.Y = Math.Max(JumpPower(jumpingMob, terrain), velocity.Y);
        }

        var bounds = mob.Dimension.CreateBBFromPosition(mob.Position);
        var next = Displace(mob, terrain, velocity, inWater || inLava ? 0.02 : gravity);
        velocity = mob.Motion;
        var displacement = next - mob.Position;
        var horizontalCollision = mob.MovementFlags.HasFlag(MovementFlags.HorizontalCollision);

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
        return next;
    }

    /// <summary>
    /// The upward velocity a jump gives <paramref name="mob"/> where it stands, like vanilla's <c>getJumpPower</c>: its
    /// jump strength by the jump factor of the block it's on (honey halves it).
    /// </summary>
    internal static float JumpPower(Mob mob, MobTerrain terrain) =>
        mob.JumpPower * BlockMovementFactors.Jump(mob, terrain);

    /// <summary>
    /// Resolves one movement against the world's collision shapes, records its fall, and restitutes collisions.
    /// Travel adds gravity and drag afterwards; externally driven movement can use the same collision boundary.
    /// </summary>
    /// <remarks>
    /// Like vanilla's <c>Entity.move</c>, a block the entity was stuck in last tick scales this move and stops it, and
    /// the block it ends up on or in (soul sand, honey) slows what's left of its horizontal velocity.
    /// </remarks>
    internal static VectorD Displace(Entity entity, MobTerrain terrain, VectorD velocity, double gravity = 0.08)
    {
        // The velocity the entity keeps after the move; a stuck entity's is gone, though it still moves a little.
        var motion = velocity;
        if (entity.TakeStuckMultiplier() is VectorD stuck)
        {
            velocity = new VectorD(velocity.X * stuck.X, velocity.Y * stuck.Y, velocity.Z * stuck.Z);
            motion = VectorD.Zero;
        }

        var bounds = entity.Dimension.CreateBBFromPosition(entity.Position);
        var stepHeight = entity is Mob
            ? Math.Clamp(entity.GetAttributeValue("minecraft:generic.step_height"), 0, 10)
            : 0;
        var grounded = entity.MovementFlags.HasFlag(MovementFlags.OnGround);
        var displacement = Collide(terrain, bounds, velocity, stepHeight, grounded, entity);
        var hitX = Math.Abs(displacement.X - velocity.X) > 1e-7;
        var hitZ = Math.Abs(displacement.Z - velocity.Z) > 1e-7;
        var hitY = displacement.Y != velocity.Y;
        var onGround = hitY && velocity.Y < 0;
        entity.MovementFlags = (onGround ? MovementFlags.OnGround : MovementFlags.None)
            | (hitX || hitZ ? MovementFlags.HorizontalCollision : MovementFlags.None);
        var next = entity.Position + displacement;
        var effectiveGravity = entity.NoGravity ? 0 : gravity;
        (Vector, IBlock)? support = null;
        var needsSupport = onGround && (entity.ContactFallDistance > 0 || displacement.Y < 0
            || !entity.Sneaking && -motion.Y > effectiveGravity);
        if (needsSupport && entity.Level is WorldData.IContactLevel contactLevel)
            support = WorldData.EntityContactPass.SupportAt(contactLevel, entity, next);

        if (entity.ContactFallDistance > 0 && displacement.MagnitudeSquared() >= 1
            && CrossesFallResetSurface(terrain, entity.Position, displacement))
        {
            entity.ResetFallDistance();
        }

        // LivingEntity refreshes fluid interaction at the resolved position before checking its fall.
        if (entity is Living && terrain.ContainsFluid(bounds.OffsetBy(displacement), water: true))
        {
            entity.ResetFallDistance();
        }
        else
        {
            entity.TrackContactFall(displacement.Y, onGround, next, support);
        }
        var motionY = motion.Y;
        if (hitY)
        {
            motionY = 0;
            if (onGround && !entity.Sneaking && -motion.Y > effectiveGravity)
            {
                var floor = support?.Item2
                    ?? terrain.GetBlock((Vector)(next - new VectorD(0, 0.2, 0)).Floor());
                var restitution = floor?.RegistryId == BlockIds.SlimeBlock ? 1f : 0f;
                if (floor is not null && TagsRegistry.Block.Beds.Entries.Contains(floor.RegistryId))
                    restitution = 0.75f;

                if (entity is not Living)
                    restitution *= 0.8f;

                if (restitution > 0)
                {
                    // 26.3 compensates for the portion of this move before impact, in vanilla's float drag.
                    var fraction = displacement.Y / velocity.Y;
                    var impactDrag = 1 + fraction * ((double)0.98f - 1);
                    motionY = (fraction * effectiveGravity - velocity.Y) * impactDrag * restitution;
                    entity.ContactBounced = true;
                }
            }
        }

        var speed = BlockMovementFactors.Speed(entity, terrain, next, onGround);
        var motionX = hitX ? 0 : motion.X * speed;
        var motionZ = hitZ ? 0 : motion.Z * speed;
        entity.Motion = new VectorD(motionX, motionY, motionZ);
        return next;
    }

    // Vanilla clips the feet's segment, capped at eight blocks, against resetting blocks and water.
    private static bool CrossesFallResetSurface(MobTerrain terrain, VectorD start, VectorD displacement)
    {
        var length = displacement.Magnitude;
        var end = start + displacement * (Math.Min(length, 8) / length);
        var direction = end - start;
        foreach (var cell in MobTerrain.CellsAlong(start, end))
        {
            if (terrain.GetBlock(cell) is not IBlock state)
                continue;

            var height = 1d;
            if (!TagsRegistry.Block.FallDamageResetting.Entries.Contains(state.RegistryId))
            {
                var fluid = WorldData.Fluids.FluidState.Of(state);
                if (!fluid.IsWater)
                    continue;

                var above = terrain.GetBlock(cell + Vector.Up);
                height = above is not null && WorldData.Fluids.FluidState.Of(above).IsWater
                    ? 1 : fluid.OwnHeight;
            }

            var surface = new BoundingBox((VectorD)cell, (VectorD)cell + new VectorD(1, height, 1));
            if (MobTerrain.RayIntersection(surface, start, direction) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// How far <paramref name="bounds"/> moves of <paramref name="velocity"/> before blocks stop it, stepping up
    /// blocks up to <paramref name="stepHeight"/> high when it stands on the ground or lands, if that gets it further.
    /// With the <paramref name="entity"/> moving, powder snow and scaffolding collide as they do for it.
    /// </summary>
    internal static VectorD Collide(
        MobTerrain terrain,
        BoundingBox bounds,
        VectorD velocity,
        float stepHeight,
        bool grounded,
        Entity? entity = null)
    {
        // Not moving, nothing clips it and it doesn't step: idle minecarts and mobs standing still ask every tick.
        if (velocity == VectorD.Zero)
            return VectorD.Zero;

        var swept = new BoundingBox(VectorD.Min(bounds.Min, bounds.Min + velocity),
            VectorD.Max(bounds.Max, bounds.Max + velocity) + new VectorD(0, stepHeight, 0));
        var shapes = collisionShapes ??= [];
        shapes.Clear();
        terrain.CollectCollisions(swept, shapes, entity);
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
