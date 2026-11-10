using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;

namespace Obsidian.WorldData.Dispensing;

internal static partial class DispenseBehaviors
{
    // Vanilla's ProjectileDispenseBehavior plays LevelEvent.SOUND_DISPENSER_PROJECTILE_LAUNCH, unless the item's
    // dispense config names another: fireworks, fire charges and wind charges do.
    private const int LaunchEvent = 1002;
    private const int FireworkEvent = 1004;
    private const int FireChargeEvent = 1018;

    // Vanilla's ProjectileItem.DispenseConfig.DEFAULT: launched from 0.7 blocks out and 0.1 up, at 1.1 blocks a tick,
    // spread by 6. Splash potions and bottles o' enchanting go a quarter faster with half the spread.
    private const float DefaultPower = 1.1f;
    private const float DefaultSpread = 6;
    private const float ThrownPower = DefaultPower * 1.25f;
    private const float ThrownSpread = DefaultSpread * 0.5f;

    // Vanilla's Projectile.getMovementToShoot: each component's spread per unit of inaccuracy.
    private const double SpreadPerUnit = 0.0172275;

    private static void RegisterProjectiles(Dictionary<int, IDispenseBehavior> behaviors)
    {
        behaviors[ItemIds.Arrow] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Arrow);
        behaviors[ItemIds.TippedArrow] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Arrow);
        behaviors[ItemIds.SpectralArrow] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Arrow);
        behaviors[ItemIds.Egg] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Egg);
        behaviors[ItemIds.BlueEgg] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Egg);
        behaviors[ItemIds.BrownEgg] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Egg);
        behaviors[ItemIds.Snowball] = new Shot(DefaultPower, DefaultSpread, LaunchEvent, Snowball);
        behaviors[ItemIds.SplashPotion] = new Shot(ThrownPower, ThrownSpread, LaunchEvent, SplashPotion);
        behaviors[ItemIds.ExperienceBottle] = new Shot(ThrownPower, ThrownSpread, LaunchEvent, ExperienceBottle);

        // Vanilla's FireChargeItem and FireworkRocketItem dispense configs.
        behaviors[ItemIds.FireCharge] = new Shot(1.0f, 6.6666665f, FireChargeEvent, FireCharge);
        behaviors[ItemIds.FireworkRocket] = new Shot(0.5f, 1.0f, FireworkEvent, Firework);
    }

    /// <summary>
    /// Vanilla's <c>Projectile.getMovementToShoot</c>: the facing, spread by a triangle draw per axis, at
    /// <paramref name="power"/> blocks a tick. Vanilla draws from the projectile's own random, which no client sees.
    /// </summary>
    private static VectorD Shoot(BlockFace facing, float power, float spread)
    {
        var random = Random.Shared;
        var deviation = SpreadPerUnit * spread;
        var direction = (VectorD)facing.ToVector();
        var spreadBy = new VectorD(
            deviation * (random.NextDouble() - random.NextDouble()),
            deviation * (random.NextDouble() - random.NextDouble()),
            deviation * (random.NextDouble() - random.NextDouble()));

        return (direction + spreadBy) * power;
    }

    // The default launch point: 0.7 blocks out and 0.1 up.
    private static VectorD LaunchPoint(DispenseSource source) => source.Out(0.7) + new VectorD(0, 0.1, 0);

    private static Entity Arrow(DispenseSource source, ItemStack stack, VectorD motion)
    {
        var spectral = stack.Holder.Id == ItemIds.SpectralArrow;
        var position = LaunchPoint(source);
        Arrow shot = spectral ? new SpectralArrow { Level = source.Level } : new Arrow { Level = source.Level };
        shot.EntityId = Server.GetNextEntityId();
        shot.Type = spectral ? EntityType.SpectralArrow : EntityType.Arrow;
        shot.Position = position;
        shot.Motion = motion;
        shot.PickupItem = new ItemStack(stack, 1);
        shot.Pickup = ArrowPickup.Allowed;
        if (spectral)
        {
            shot.Effect = (int)PotionEffect.Glowing - 1;
            shot.EffectDuration = 200;
        }

        return shot;
    }

    private static Entity Egg(DispenseSource source, ItemStack stack, VectorD motion)
    {
        var position = LaunchPoint(source);
        var egg = new Egg
        {
            Level = source.Level,
            EntityId = Server.GetNextEntityId(),
            Position = position,
            Motion = motion,
            Item = new ItemStack(stack, 1)
        };
        return egg;
    }

    private static Entity Snowball(DispenseSource source, ItemStack stack, VectorD motion) =>
        new MobProjectile(source.Level, EntityType.Snowball, LaunchPoint(source), motion);

    private static Entity SplashPotion(DispenseSource source, ItemStack stack, VectorD motion)
    {
        var contents = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        return new MobProjectile(source.Level, EntityType.SplashPotion, LaunchPoint(source), motion)
        {
            SplashEffects = [.. ArrowPotionEffects.Get(contents)],
            SplashItem = new ItemStack(stack, 1)
        };
    }

    private static Entity ExperienceBottle(DispenseSource source, ItemStack stack, VectorD motion)
    {
        var position = LaunchPoint(source);
        var bottle = new ThrownExperienceBottle
        {
            Level = source.Level,
            EntityId = Server.GetNextEntityId(),
            Position = position,
            Motion = motion,
            Item = new ItemStack(stack, 1)
        };
        return bottle;
    }

    // Vanilla's FireChargeItem.asProjectile: a small fireball a block out. It draws a direction from the level's random
    // (kept here, since the level's random is shared), which the shot that follows replaces.
    private static Entity FireCharge(DispenseSource source, ItemStack stack, VectorD motion)
    {
        const double spread = 0.11485000000000001;

        var random = source.Level.GrowthRandom;
        var step = source.Facing.ToVector();
        random.Triangle(step.X, spread);
        random.Triangle(step.Y, spread);
        random.Triangle(step.Z, spread);

        return new MobProjectile(source.Level, EntityType.SmallFireball, source.Out(1.0), motion);
    }

    // Vanilla's FireworkRocketItem: a rocket just outside the dispenser, shot at an angle, which is launched with its
    // sound on its first tick.
    private static Entity Firework(DispenseSource source, ItemStack stack, VectorD motion)
    {
        var position = source.Out(0.5000099999997474);
        var rocket = new FireworkRocket
        {
            Level = source.Level,
            EntityId = Server.GetNextEntityId(),
            Item = new ItemStack(stack, 1),
            Position = position,
            Motion = motion,
            ShotAtAngle = true
        };
        return rocket;
    }

    /// <summary>
    /// Vanilla's <c>ProjectileDispenseBehavior</c>: shoots the item as its projectile along the facing, with the
    /// item's launch event.
    /// </summary>
    /// <remarks>
    /// The projectile's <c>projectile_shoot</c> game event has no source and is heard where the projectile is after
    /// its first move, as vanilla's projectile emits it on its first tick.
    /// </remarks>
    private sealed class Shot(float power, float spread, int launchEvent,
        Func<DispenseSource, ItemStack, VectorD, Entity> create) : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var motion = Shoot(source.Facing, power, spread);
            var projectile = create(source, stack, motion);
            if (!level.TrySpawnEntity(projectile, hostOwned: true))
                return new Dispensed(stack, launchEvent);

            return new Dispensed(DispenseSource.Shrink(stack), launchEvent);
        }

    }
}
