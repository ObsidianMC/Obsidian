using Obsidian.API.Inventory;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData;
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

    // Vanilla's wither skeleton gets a stone sword, no armour and no enchantments, drawing nothing.
    private protected override void PopulateBareEquipment(IRandomSource random, float special) =>
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(ItemsRegistry.StoneSword));

    // Vanilla's WitherSkeleton.finalizeSpawn: the skeleton's setup, then an attack damage of 4.
    internal override void FinalizeBareSpawn(AbstractLevel level, IRandomSource random)
    {
        base.FinalizeBareSpawn(level, random);
        TryUpdateAttribute("minecraft:generic.attack_damage", 4);
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        TryUpdateAttribute("minecraft:generic.attack_damage", 4);
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(ItemsRegistry.StoneSword));
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
        this.DropVanillaLoot(source);
        foreach (var slot in Enum.GetValues<Obsidian.API.Inventory.EquipmentSlot>())
        {
            var item = this.GetEquipment(slot);
            if (!item.IsAir && this.Random.NextSingle() < this.GetEquipmentDropChance(slot))
                this.DropItem(item);
        }

        return default;
    }
}
