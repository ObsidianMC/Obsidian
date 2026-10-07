using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombie_nautilus")]
public sealed partial class ZombieNautilus : Nautilus
{
    public int Variant { get; set; }
    public ZombieNautilus() => Type = EntityType.ZombieNautilus;
    public override bool IsBaby { get => false; set { } }
    internal override bool CanBreed => false;
    internal override bool Hostile => true;
    internal override bool PanicsWhenHurt => false;
    protected override string? SoundName => "zombie_nautilus";
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new MeleeAttackGoal(this, 1.2f));
        actions.AddGoal(3, new TemptGoal(this, CanEat, 1.25f));
        actions.AddGoal(6, new RandomStrollGoal(this, 0.5f));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => Owner == Guid.Empty && entity is IPlayer && IsInRange(entity, 16)));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (Owner != Guid.Empty) AttackTarget = null;
        if (Level.LevelData.Difficulty == Difficulty.Peaceful && Owner == Guid.Empty)
        { await RemoveAsync(); return; }
        if (!InWater && Level.DimensionName == "minecraft:overworld" && Level.DayTime is >= 0 and < 12000 &&
            !Level.LevelData.Raining && Terrain.GetSkyLight((Vector)EyePosition.Floor()) == 15 && GetEquipment(EquipmentSlot.Body).IsAir)
            Ignite(8);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(20, EntityMetadataType.ZombieNautilusVariant);
        writer.WriteVarInt(Variant);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        base.WriteAdditionalSave(writer);
        writer.WriteString("variant", Variant == 1 ? "minecraft:warm" : "minecraft:temperate");
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        base.ReadAdditionalSave(tag);
        Variant = tag.TryGetTagValue<string>("variant", out var variant) && variant == "minecraft:warm" ? 1 : 0;
    }
}
