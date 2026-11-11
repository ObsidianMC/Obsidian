using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

/// <summary>Cactus pricks anything damageable, including item entities, throughout its cell.</summary>
internal sealed class CactusContactBehavior : IEntityInsideBehavior
{
    public async ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        if (entity is ItemEntity item)
            await item.HurtByCactusAsync();
        else if (entity is VehicleEntity vehicle)
        {
            await vehicle.HurtAsync(null, 1, VehicleDamage.Generic);
            if (vehicle.IsRemoved && level.Level is AbstractLevel world)
                world.EmitGameEvent(MobGameEvent.EntityDie, vehicle.Position, vehicle);
        }
        else if (entity is Living living and not ArmorStand)
            await living.DamageCombatAsync(null, 1, CombatDamageKind.Cactus);
    }
}

/// <summary>Lit campfires deal fire damage to living entities without igniting ordinary mobs or items.</summary>
internal sealed class CampfireContactBehavior : IEntityInsideBehavior
{
    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        if (block.GetProperty("lit") != "true" || entity is not Living living || living.IsFireImmune
            || living.HasPotionEffect((int)PotionEffect.FireResistance - 1))
        {
            return default;
        }

        if (entity is ArmorStand stand)
            return stand.BurnFromCampfireAsync();

        foreach (var (item, slot) in CombatEffects.Armor(living))
        {
            if (slot == "feet" && CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.FrostWalker) > 0)
                return default;
        }

        var damage = block.RegistryId == BlockIds.SoulCampfire ? 2 : 1;
        return living.DamageCombatAsync(null, damage, CombatDamageKind.Campfire);
    }
}

/// <summary>Falling beside honey clamps the fall, damps a fast slide and sometimes plays slide effects.</summary>
internal sealed class HoneySlideBehavior : IEntityInsideBehavior
{
    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        var location = entity.Position;
        var motion = entity.Motion;
        var oldY = motion.Y / (double)0.98f + 0.08;
        var edge = 0.4375 + entity.Dimension.Width / 2f;
        var beside = Math.Abs(position.X + 0.5 - location.X) + 1e-7 > edge
            || Math.Abs(position.Z + 0.5 - location.Z) + 1e-7 > edge;
        if ((entity.MovementFlags & MovementFlags.OnGround) != 0 || location.Y > position.Y + 0.9375 - 1e-7
            || oldY >= -0.08 || !beside)
        {
            return default;
        }

        // Vanilla undoes living travel's gravity/drag even for items and no-gravity entities; these constants and
        // the -0.13 boundary determine the captured motion and must retain double precision with float drag.
        var damping = oldY < -0.13 ? -0.05 / oldY : 1;
        entity.Motion = new VectorD(motion.X * damping, (-0.05 - 0.08) * (double)0.98f, motion.Z * damping);
        entity.ResetFallDistance();

        if (entity is not (Living or PrimedTnt or Boat) || level.Level is not AbstractLevel world)
            return default;

        // Vanilla draws sound first, then the client particle event, including when a silent entity slides.
        var random = level.Blocks.Random;
        if (random.NextInt(5) == 0 && !entity.Silent)
        {
            var category = entity switch
            {
                Mob mob => mob.ContactSoundCategory,
                IPlayer => SoundCategory.Players,
                _ => SoundCategory.Neutral
            };
            LevelSounds.Play(world, entity.Position, "minecraft:block.honey_block.slide", category);
        }
        if (random.NextInt(5) == 0)
        {
            entity.PacketBroadcaster.QueuePacketToLevelInRange(world, entity.Position,
                new EntityEventPacket { EntityId = entity.EntityId, Event = 53 });
        }

        return default;
    }
}

/// <summary>The hopper's contact path shares its tick's transfer code and cooldown.</summary>
internal sealed class HopperContactBehavior : IEntityInsideBehavior
{
    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity) =>
        entity is ItemEntity item && level.Level is AbstractLevel world
            && level.Blocks.GetBlockEntity(position) is HopperBlockEntity hopper
            ? Hoppers.TouchItemAsync(world, hopper, item) : default;
}
