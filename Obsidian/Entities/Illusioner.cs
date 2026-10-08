using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:illusioner")]
public sealed partial class Illusioner : Skeleton
{
    private int spellTicks;
    private byte spell;
    private long nextMirror;
    private long nextBlindness;
    private Guid blindedTarget;
    public Illusioner() => Type = EntityType.Illusioner;
    internal override bool Hostile => true;
    protected override bool BurnsInSun => false;
    protected override string? SoundName => "illusioner";
    internal override int AttackInterval => 20;
    protected override void FinalizeSpawn() => SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.Bow));
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new IllusionerCastingGoal(this));
        actions.AddGoal(6, new BowAttackGoal(this));
        actions.AddGoal(8, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 3));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.Villager or EntityType.IronGolem or EntityType.WanderingTrader));
    }
    internal bool Casting => spellTicks > 0;
    protected override ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return default;
        if (spellTicks > 0)
        {
            if (--spellTicks == 0)
            {
                if (spell == 4) AddPotionEffect((int)PotionEffect.Invisibility - 1, 1200);
                else if (spell == 5 && AttackTarget is Living target && IsValidTarget(target))
                    target.AddPotionEffect((int)PotionEffect.Blindness - 1, 400);
                PlayMobSound("cast_spell");
                spell = 0;
                SynchronizeMetadata();
            }
        }
        else if (AttackTarget is { } enemy && IsValidTarget(enemy))
        {
            if (AiTick >= nextMirror && !HasPotionEffect((int)PotionEffect.Invisibility - 1))
            {
                nextMirror = AiTick + 340;
                BeginSpell(4, "prepare_mirror");
            }
            else if (AiTick >= nextBlindness && enemy.Uuid != blindedTarget && Level.LevelData.Difficulty == Difficulty.Hard)
            {
                blindedTarget = enemy.Uuid;
                nextBlindness = AiTick + 180;
                BeginSpell(5, "prepare_blindness");
            }
        }
        return default;
    }
    private void BeginSpell(byte kind, string sound)
    {
        spell = kind;
        spellTicks = 20;
        PlayMobSound(sound);
        SynchronizeMetadata();
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
        writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte(spell);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteInt("SpellTicks", spellTicks);
    protected override void ReadAdditionalSave(NbtCompound tag) => spellTicks = Math.Clamp(tag.TryGetTagValue<int>("SpellTicks", out var ticks) ? ticks : 0, 0, 20);
}

internal sealed class IllusionerCastingGoal(Illusioner illusioner) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => illusioner.Casting;
    public override void Start() { ((Navigator)illusioner.Navigator!).Stop(); illusioner.MoveControl.Stop(); }
    public override ValueTask TickAsync() { if (illusioner.AttackTarget is { } target) illusioner.LookControl.LookAt(target); return default; }
}
