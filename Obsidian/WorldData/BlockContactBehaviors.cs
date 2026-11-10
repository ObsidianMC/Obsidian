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
