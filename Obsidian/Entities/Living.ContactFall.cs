using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Living
{
    internal override async ValueTask CauseContactFallAsync(double distance, float multiplier = 1)
    {
        if (this is Mob mob && !mob.ContactFallDamage)
            return;

        // Armor stands ignore the fall damage source in vanilla.
        if (this is ArmorStand)
            return;

        var damage = this.ContactFallDamageAmount(distance, multiplier);
        if (damage <= 0)
            return;

        if (this.Level is AbstractLevel world)
        {
            var category = this is Mob withSound ? withSound.ContactSoundCategory : SoundCategory.Players;
            var sound = damage > 4 ? "big_fall" : "small_fall";
            LevelSounds.Play(world, this.Position, $"minecraft:entity.generic.{sound}", category);
            var below = (Vector)(this.Position - new VectorD(0, 0.2, 0)).Floor();
            var block = world.BlockUpdates.GetBlock(below);
            if (!block.IsAir)
            {
                var sounds = BlockSoundsRegistry.Get(block.GetHashCode());
                LevelSounds.Play(world, this.Position, sounds.Fall.ResourceLocation, category,
                    sounds.Volume * 0.5f, sounds.Pitch * 0.75f);
            }
        }

        await this.DamageCombatAsync(this, (float)damage, CombatDamageKind.Fall);
    }

    internal double ContactFallDamageAmount(double distance, float multiplier)
    {
        var safe = this.GetAttributeValue("minecraft:safe_fall_distance");
        if (this is Mob mob && !this.HasAttribute("minecraft:safe_fall_distance"))
            safe = mob.ContactSafeFallDistance;

        var damageMultiplier = this.GetAttributeValue("minecraft:fall_damage_multiplier");
        // 26.3 adds the tolerance before subtracting safe distance, then floors after both multipliers.
        return Math.Floor((distance + 1e-6 - safe) * multiplier * damageMultiplier);
    }
}

public partial class Mob
{
    internal SoundCategory ContactSoundCategory => this.MobSoundCategory;

    internal bool ContactFallDamage => this.TakesFallDamage && this is not Chicken;

    internal float ContactSafeFallDistance => this.SafeFallDistance;

    internal override ValueTask CauseContactFallAsync(double distance, float multiplier = 1)
    {
        if (this is Creeper creeper && distance > 0)
        {
            var increase = (int)(distance * 1.5);
            creeper.FuseTicks = Math.Max(0, Math.Min(creeper.Fuse - 5, creeper.FuseTicks + increase));
        }

        return base.CauseContactFallAsync(distance, multiplier);
    }

    internal override double ContactFallDistance
    {
        get => this.fallDistance;
        set => this.fallDistance = value;
    }
}

public partial class PlayerBase
{
    internal override double ContactFallDistance
    {
        get => this.FallDistance;
        set => this.FallDistance = (float)value;
    }
}
