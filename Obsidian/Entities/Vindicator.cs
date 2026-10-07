using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:vindicator")]
public sealed partial class Vindicator : PathfinderMob
{
    public bool Johnny { get; set; }
    public Vindicator() => Type = EntityType.Vindicator;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "vindicator";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(EquipmentSlot.MainHand));

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        ((Navigator)Navigator!).CanOpenDoors = true;
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(4, new MeleeAttackGoal(this));
        actions.AddGoal(8, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 3));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.Villager or EntityType.IronGolem or EntityType.WanderingTrader));
        targets.AddGoal(4, new NearestAttackableTargetGoal(this, target => Johnny && target is Living &&
            target.Type is not (EntityType.Vindicator or EntityType.Pillager or EntityType.Evoker or EntityType.Illusioner)));
    }

    protected override void FinalizeSpawn() => SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.IronAxe));
    protected override ValueTask TickMobAsync()
    {
        if (CustomName?.Text == "Johnny") Johnny = true;
        return default;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (source is IPlayer) DropItem(Material.Emerald, Random.Next(2));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot))
                DropItem(GetEquipment(slot));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteBool("Johnny", Johnny || CustomName?.Text == "Johnny");
    protected override void ReadAdditionalSave(NbtCompound tag) => Johnny = tag.TryGetBool("Johnny", out var johnny) && johnny;
}
