using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:strider")]
public sealed partial class Strider : FarmAnimal
{
    private bool cold;
    private int boostTicks;
    private int boostDuration;
    public Strider() => Type = EntityType.Strider;
    protected override bool UsesFloatGoal => false;
    protected override bool WaterSensitive => true;
    protected override string? SoundName => "strider";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.WarpedFungus };
    internal override float MovementSpeed => base.MovementSpeed * (cold ? 0.66f : 1);
    protected override VectorD Travel()
    {
        var gravity = NoGravity;
        if (InLava)
        {
            NoGravity = true;
            var above = Terrain.GetBlock((Vector)(Position + new VectorD(0, 0.6, 0)).Floor())?.Material == Material.Lava;
            if (above) Motion = new VectorD(Motion.X, Math.Max(Motion.Y, 0.1), Motion.Z);
            else { Motion = new VectorD(Motion.X, Math.Max(0, Motion.Y), Motion.Z); MovementFlags |= MovementFlags.OnGround; }
        }
        var position = base.Travel(); NoGravity = gravity; return position;
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        var nowCold = !InLava && !TagsRegistry.Block.StriderWarmBlocks.Entries.Contains(Terrain.GetBlock((Vector)Position.Floor())?.RegistryId ?? -1);
        if (cold != nowCold) { cold = nowCold; SynchronizeMetadata(); }
        if (boostDuration > 0 && ++boostTicks > boostDuration) { boostTicks = boostDuration = 0; SynchronizeMetadata(); }
        if (InLava && Rider == null && !MobBitMask.HasFlag(MobBitmask.NoAi))
        {
            var player = Level.GetPlayersInRange(Position, 10).FirstOrDefault(player => CanEat(player.GetHeldItem()) || CanEat(player.GetOffHandItem()));
            if (player != null) MoveControl.MoveTo(player.Position, 1.4f);
        }
    }
    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (CanEat(item)) { await base.FeedAsync(player, hand); return; }
        if (IsBaby) return;
        if (item is { Count: > 0, Type: Material.Saddle } && GetEquipment(EquipmentSlot.Saddle).IsAir)
        { SetEquipment(EquipmentSlot.Saddle, new ItemStack(item)); await ConsumeInteractionItemAsync(player, hand); }
        else if (!player.Sneaking && !GetEquipment(EquipmentSlot.Saddle).IsAir && player is Player rider && Mount(rider)) GoalController?.Pause();
    }
    internal bool Boost(Player player)
    {
        if (!ReferenceEquals(Rider, player) || boostDuration > 0) return false;
        boostDuration = Random.Next(140, 981); boostTicks = 0; SynchronizeMetadata(); return true;
    }
    protected override void TickRidden()
    {
        base.TickRidden();
        if (Rider == null) { if (GoalController is GoalSelector { IsPaused: true } goals) goals.Resume(); return; }
        if (Rider.GetHeldItem()?.Type != Material.WarpedFungusOnAStick && Rider.GetOffHandItem()?.Type != Material.WarpedFungusOnAStick) return;
        Yaw = Rider.Yaw;
        var boost = boostDuration > 0 ? 1 + 1.15f * MathF.Sin((float)boostTicks / boostDuration * MathF.PI) : 1;
        MoveControl.Ride(MovementSpeed * 0.55f * boost);
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    { if (!IsBaby) DropItem(Material.String, Random.Next(2, 6)); if (!GetEquipment(EquipmentSlot.Saddle).IsAir) DropItem(GetEquipment(EquipmentSlot.Saddle)); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt); writer.WriteVarInt(boostDuration);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(cold);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteBool("Suffocating", cold);
        writer.WriteInt("ObsidianBoostTicks", boostTicks);
        writer.WriteInt("ObsidianBoostDuration", boostDuration);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        cold = tag.TryGetBool("Suffocating", out var saved) && saved;
        boostDuration = tag.TryGetTagValue<int>("ObsidianBoostDuration", out var duration) ? Math.Clamp(duration, 0, 980) : 0;
        boostTicks = tag.TryGetTagValue<int>("ObsidianBoostTicks", out var ticks) ? Math.Clamp(ticks, 0, boostDuration) : 0;
    }
}
