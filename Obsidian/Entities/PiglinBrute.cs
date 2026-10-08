using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:piglin_brute")]
public sealed partial class PiglinBrute : PathfinderMob
{
    public bool IsImmuneToZombification { get; set; }
    internal int TimeInOverworld { get; set; }
    public PiglinBrute() => Type = EntityType.PiglinBrute;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override bool CanDespawn => false;
    protected override string? SoundName => "piglin_brute";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 20;
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(EquipmentSlot.MainHand));
    protected override void FinalizeSpawn()
    {
        PersistenceRequired = true;
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.GoldenAxe));
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        ((Navigator)Navigator!).CanOpenDoors = true;
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(2, new MeleeAttackGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(7, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer && IsInRange(target, 12)));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.WitherSkeleton or EntityType.Wither));
    }
    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        var safe = CodecRegistry.TryGetDimension(Level.DimensionName, out var dimension) ? dimension.Element.PiglinSafe : Level.DimensionName == "minecraft:the_nether";
        if (safe || IsImmuneToZombification) { TimeInOverworld = 0; return; }
        if (++TimeInOverworld > 300)
        {
            PlayMobSound("converted_to_zombified");
            var zombie = (ZombifiedPiglin)await ConvertToAsync(EntityType.ZombifiedPiglin);
            zombie.AddPotionEffect((int)PotionEffect.Nausea - 1, 200);
        }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot)) DropItem(GetEquipment(slot));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsImmuneToZombification);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteBool("IsImmuneToZombification", IsImmuneToZombification);
        writer.WriteInt("TimeInOverworld", TimeInOverworld);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        IsImmuneToZombification = tag.TryGetBool("IsImmuneToZombification", out var immune) && immune;
        TimeInOverworld = Math.Max(0, tag.TryGetTagValue<int>("TimeInOverworld", out var ticks) ? ticks : 0);
    }
}
