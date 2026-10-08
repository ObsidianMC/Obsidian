using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:wither_skeleton")]
public sealed partial class WitherSkeleton : Skeleton
{
    public WitherSkeleton() => Type = EntityType.WitherSkeleton;
    internal override bool Hostile => true;
    protected override bool BurnsInSun => false;
    protected override string? SoundName => "wither_skeleton";
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(EquipmentSlot.MainHand));

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.Piglin or EntityType.PiglinBrute));
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        TryUpdateAttribute("minecraft:generic.attack_damage", 4);
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.StoneSword));
    }

    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        var health = target.Health;
        await base.PerformMeleeAttackAsync(target);
        if (target.Health < health && target is Living living)
            living.AddPotionEffect((int)PotionEffect.Wither - 1, 200, effect: EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.Bone, Random.Next(3));
        DropItem(Material.Coal, Math.Max(0, Random.Next(3) - 1));
        if (source is IPlayer && Random.NextSingle() < 0.025f)
            DropItem(Material.WitherSkeletonSkull);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot))
                DropItem(GetEquipment(slot));
        return default;
    }
}
