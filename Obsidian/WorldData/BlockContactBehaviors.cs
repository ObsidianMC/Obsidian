using Obsidian.API.Blocks;
using Obsidian.Entities;

namespace Obsidian.WorldData;

internal sealed class MagmaContactBehavior : IStepOnBehavior
{
    public ValueTask SteppedOnAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        if (entity is not Living living || entity is ArmorStand || entity.Sneaking || living.IsFireImmune
            || living.HasPotionEffect((int)PotionEffect.FireResistance - 1))
        {
            return default;
        }

        // Frost Walker's damage_immunity effect applies only in its feet equipment slot, to burn_from_stepping.
        foreach (var (item, slot) in CombatEffects.Armor(living))
        {
            if (slot == "feet" && CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.FrostWalker) > 0)
                return default;
        }

        return living.DamageCombatAsync(living, 1, CombatDamageKind.HotFloor);
    }
}

internal sealed class FarmlandContactBehavior : IFallOnBehavior
{
    public ValueTask FallenOnAsync(
        IContactLevel level, Vector position, IBlock block, Entity entity,
        double fallDistance, VectorD impactPosition)
    {
        // Vanilla draws before checking the entity's kind, griefing permission or volume.
        var chance = level.Blocks.Random.NextFloat();
        var size = entity.Dimension;
        var mayTrample = entity is IPlayer || level.Level.LevelData.GetBooleanRule("mob_griefing");
        if (chance < fallDistance - 0.5 && entity is Living && mayTrample
            && size.Width * size.Width * size.Height > 0.512f)
        {
            FarmlandBlockBehavior.TurnToDirt(level.Blocks, position, block, entity);
        }

        return entity.CauseContactFallAsync(fallDistance, impact: impactPosition);
    }
}

internal sealed class TurtleEggContactBehavior : IFallOnBehavior, IStepOnBehavior
{
    public ValueTask FallenOnAsync(
        IContactLevel level, Vector position, IBlock block, Entity entity,
        double fallDistance, VectorD impactPosition)
    {
        // All Zombie subclasses, including zombie villagers and zombified piglins, skip the fall draw.
        if (entity is not Zombie)
            TryBreak(level, position, block, entity, 3);

        return entity.CauseContactFallAsync(fallDistance, impact: impactPosition);
    }

    public ValueTask SteppedOnAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        if (!entity.Sneaking)
            TryBreak(level, position, block, entity, 100);

        return default;
    }

    private static void TryBreak(IContactLevel level, Vector position, IBlock block, Entity entity, int odds)
    {
        if (entity is not Living || entity is Turtle or Bat
            || entity is not IPlayer && !level.Level.LevelData.GetBooleanRule("mob_griefing"))
        {
            return;
        }

        var random = level.Blocks.Random;
        if (random.NextInt(odds) != 0 || level.Level is not AbstractLevel world)
            return;

        var pitch = 0.9f + random.NextFloat() * 0.2f;

        // A landing can be dispatched after the eggs changed, and another thread can change them meanwhile: check
        // and break as one short mutation, as farmland does. The draws above stay outside it, in vanilla's order.
        lock (world.Fluids.TickLock)
        {
            if (!level.Blocks.GetBlock(position).IsSameState(block))
                return;

            BreakEgg(level, world, position, block, pitch);
        }
    }

    // Vanilla's TurtleEggBlock.destroyEgg once the chance passed: one egg fewer, or the block gone with the last one.
    private static void BreakEgg(IContactLevel level, AbstractLevel world, Vector position, IBlock block, float pitch)
    {
        LevelSounds.PlayAtBlock(world, position, "minecraft:entity.turtle.egg_break", 0.7f, pitch);
        var count = int.Parse(block.GetProperty("eggs")!);
        if (count > 1)
        {
            level.Blocks.SetBlock(position, block.WithProperty("eggs", count - 1), BlockUpdateFlags.Clients);
            world.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)position + 0.5, affectedBlock: block);
            world.BroadcastLevelEvent(2001, position, block.GetHashCode());
        }
        else
        {
            // destroyBlock(false): particles before the write, no loot, and a game event without an entity source.
            world.BroadcastLevelEvent(2001, position, block.GetHashCode());
            level.Blocks.RemoveBlock(position);
            world.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)position + 0.5, affectedBlock: block);
        }
    }
}

/// <summary>
/// Vanilla's <c>SlimeBlock.stepOn</c>: walking on slime, with little vertical movement and not sneaking, damps the
/// entity's horizontal velocity, the more so the flatter it moves.
/// </summary>
internal sealed class SlimeStepBehavior : IStepOnBehavior
{
    public ValueTask SteppedOnAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        var motion = entity.Motion;
        var vertical = Math.Abs(motion.Y);
        if (vertical >= 0.1 || entity.Sneaking)
            return default;

        var damping = 0.4 + vertical * 0.2;
        entity.Motion = new VectorD(motion.X * damping, motion.Y, motion.Z * damping);
        return default;
    }
}

internal sealed class SoftLandingBehavior(float multiplier, bool honey = false, float distanceScale = 1)
    : IFallOnBehavior
{
    public async ValueTask FallenOnAsync(
        IContactLevel level, Vector position, IBlock block, Entity entity,
        double fallDistance, VectorD impactPosition)
    {
        if (honey && level.Level is AbstractLevel world)
        {
            LevelSounds.Play(world, impactPosition, "minecraft:block.honey_block.slide", SoundCategory.Neutral);
            entity.PacketBroadcaster.QueuePacketToLevelInRange(world, entity.Position,
                new Net.Packets.Play.Clientbound.EntityEventPacket { EntityId = entity.EntityId, Event = 54 });
        }

        // Slime's fall hook skips causeFallDamage entirely when bounce is suppressed in 26.3.
        if (multiplier == 0 && entity.Sneaking)
            return;

        var distance = fallDistance * distanceScale;
        await entity.CauseContactFallAsync(distance, multiplier, impactPosition);
        if (honey && entity is Living living && living.ContactFallDamageAmount(distance, multiplier) > 0
            && level.Level is AbstractLevel honeyLevel)
        {
            var sounds = BlockSoundsRegistry.Get(block.GetHashCode());
            var category = entity is Mob mob ? mob.ContactSoundCategory : SoundCategory.Neutral;
            LevelSounds.Play(honeyLevel, impactPosition, sounds.Fall.ResourceLocation, category,
                sounds.Volume * 0.5f, sounds.Pitch * 0.75f);
        }
    }
}

/// <summary>
/// Vanilla's <c>WebBlock.entityInside</c>: anything in a cobweb moves a quarter as fast across it and a twentieth as
/// fast up or down, or half and a quarter with Weaving.
/// </summary>
internal sealed class CobwebContactBehavior : IEntityInsideBehavior
{
    private static readonly VectorD stuck = new(0.25, 0.05f, 0.25);

    private static readonly VectorD weaving = new(0.5, 0.25, 0.5);

    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        var weaves = entity is Living living && living.HasPotionEffect((int)PotionEffect.Weaving - 1);
        entity.MakeStuckInBlock(weaves ? weaving : stuck);
        return default;
    }
}

/// <summary>
/// Vanilla's <c>SweetBerryBushBlock.entityInside</c>: a bush slows anything living but foxes and bees, and once it has
/// grown past its first stage it pricks them while they move through it.
/// </summary>
internal sealed class SweetBerryBushContactBehavior : IEntityInsideBehavior
{
    private static readonly VectorD stuck = new(0.8f, 0.75, 0.8f);

    // How far across the entity must have moved on one axis this tick to be pricked.
    private const double PrickingMovement = 0.003f;

    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        if (entity is not Living living || entity.Type is EntityType.Fox or EntityType.Bee)
            return default;

        entity.MakeStuckInBlock(stuck);
        if (block.GetProperty("age") == "0")
            return default;

        var movement = entity.TickMovement;
        var moved = movement.X * movement.X + movement.Z * movement.Z > 0;
        var far = Math.Abs(movement.X) >= PrickingMovement || Math.Abs(movement.Z) >= PrickingMovement;
        if (!moved || !far)
            return default;

        return living.DamageCombatAsync(living, 1, CombatDamageKind.SweetBerryBush);
    }
}

/// <summary>
/// Vanilla's <c>PowderSnowBlock</c> touching entities: whatever is in it sinks slowly (something living only while its
/// feet are in it), and a fall into it hurts nothing, though a long one is heard.
/// </summary>
/// <remarks>
/// What it stands on is its collision (see <c>EntityCollisionShapes</c>); freezing is the entity's own tick.
/// </remarks>
internal sealed class PowderSnowContactBehavior : IEntityInsideBehavior, IFallOnBehavior
{
    /// <summary>The one instance, which powder snow's inside and fall hooks share.</summary>
    public static PowderSnowContactBehavior Instance { get; } = new();

    private static readonly VectorD stuck = new(0.9f, 1.5, 0.9f);

    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        var feet = level.Blocks.GetBlock((Vector)entity.Position.Floor());
        if (entity is Living && feet.RegistryId != BlockIds.PowderSnow)
            return default;

        entity.MakeStuckInBlock(stuck);

        // Vanilla draws the snowflakes a moving entity kicks up from the level's random, which later draws depend on,
        // so they're drawn here too; the particles aren't sent yet.
        var movement = entity is PlayerBase ? entity.TickMovement : entity.Motion;
        if (movement.X != 0 || movement.Z != 0)
        {
            var random = level.Blocks.Random;
            if (random.NextBoolean())
            {
                random.NextFloat();
                random.NextFloat();
            }
        }

        return default;
    }

    public ValueTask FallenOnAsync(
        IContactLevel level, Vector position, IBlock block, Entity entity,
        double fallDistance, VectorD impactPosition)
    {
        // It replaces the fall's damage: only something living that fell at least four blocks is heard landing.
        if (fallDistance < 4 || entity is not Living || entity.Silent || level.Level is not AbstractLevel world)
            return default;

        var sound = fallDistance < 7 ? "small_fall" : "big_fall";
        var category = entity is Mob mob ? mob.ContactSoundCategory : SoundCategory.Players;
        LevelSounds.Play(world, impactPosition, $"minecraft:entity.generic.{sound}", category);
        return default;
    }
}
