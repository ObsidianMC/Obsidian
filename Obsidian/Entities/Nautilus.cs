using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using System.Threading;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:nautilus")]
public partial class Nautilus : FarmAnimal
{
    public Guid Owner { get; private set; }
    private int dashCooldown;
    private int dashTicks;
    private int pendingDashCharge = -1;
    public Nautilus() => Type = EntityType.Nautilus;
    internal override bool SwimmingNavigation => true;
    protected override bool UsesFloatGoal => false;
    protected override string? SoundName => "nautilus";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0 } && TagsRegistry.Item.NautilusFood.Entries.Contains(item.Holder.Id);
    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    protected override ValueTask TickAirSupplyAsync() { Air = 300; return default; }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(5, new NautilusFollowOwnerGoal(this));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (dashCooldown > 0 && --dashCooldown == 0) PlayMobSound("dash_ready");
        if (dashTicks > 0 && --dashTicks == 0) SynchronizeMetadata();
        if (!InWater && AiTick % 20 == 0) await DamageEnvironmentAsync(1);
        if (Rider != null && InWater && AiTick % 20 == 0)
            Rider.AddPotionEffect((int)PotionEffect.BreathOfTheNautilus - 1, 200, 0, EntityEffectFlags.ShowIcon);
    }
    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (CanEat(held))
        {
            if (Owner == Guid.Empty && !IsBaby)
            {
                await ConsumeInteractionItemAsync(player, hand);
                if (Random.Next(3) == 0) { Owner = player.Uuid; PersistenceRequired = true; SendEntityEvent(7); SynchronizeMetadata(); }
                else SendEntityEvent(6);
            }
            else if (Health < GetAttributeValue("minecraft:generic.max_health"))
            { Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 2); await ConsumeInteractionItemAsync(player, hand); }
            else if (Type == EntityType.Nautilus) await base.FeedAsync(player, hand);
            return;
        }
        if (Owner == Guid.Empty || IsBaby) return;
        if (held is { Count: > 0, Type: Material.Saddle } && GetEquipment(EquipmentSlot.Saddle).IsAir)
        { SetEquipment(EquipmentSlot.Saddle, new ItemStack(held)); await ConsumeInteractionItemAsync(player, hand); }
        else if (held is { Count: > 0 } && held.Holder.UnlocalizedName.EndsWith("_nautilus_armor", StringComparison.Ordinal))
        {
            if (!GetEquipment(EquipmentSlot.Body).IsAir) DropItem(GetEquipment(EquipmentSlot.Body));
            SetEquipment(EquipmentSlot.Body, new ItemStack(held)); await ConsumeInteractionItemAsync(player, hand);
        }
        else if (!player.Sneaking && !GetEquipment(EquipmentSlot.Saddle).IsAir && player is Player rider && Mount(rider)) GoalController?.Pause();
    }
    protected override void TickRidden()
    {
        base.TickRidden();
        var charge = Interlocked.Exchange(ref pendingDashCharge, -1);
        if (Rider == null) { if (GoalController is GoalSelector { IsPaused: true } goals) goals.Resume(); return; }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        Yaw = Rider.Yaw; Pitch = Rider.Pitch;
        var forward = (Rider.Input.HasFlag(PlayerInput.Forward) ? 1 : 0) - (Rider.Input.HasFlag(PlayerInput.Backward) ? 1 : 0);
        if (InWater) MoveControl.Acceleration = (VectorD)Rider.GetLookDirection() * (forward * 0.025);
        else MoveControl.Ride(MovementSpeed, forward);
        if (charge >= 0 && dashCooldown == 0)
        {
            var strength = charge >= 90 ? 1 : 0.4f + 0.4f * charge / 90;
            Motion += (VectorD)Rider.GetLookDirection() * ((InWater ? 1.2 : 0.5) * strength);
            dashCooldown = 40; dashTicks = 10; SynchronizeMetadata(); PlayMobSound("dash");
        }
    }
    internal bool RequestDash(IPlayer player, int charge) => ReferenceEquals(Rider, player) && !IsBaby &&
        !GetEquipment(EquipmentSlot.Saddle).IsAir && dashCooldown == 0 && !MobBitMask.HasFlag(MobBitmask.NoAi) &&
        Interlocked.CompareExchange(ref pendingDashCharge, Math.Clamp(charge, 0, 100), -1) == -1;
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        foreach (var slot in new[] { EquipmentSlot.Body, EquipmentSlot.Saddle }) if (!GetEquipment(slot).IsAir) DropItem(GetEquipment(slot));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte); writer.WriteByte((byte)(Owner != Guid.Empty ? 4 : 0));
        writer.WriteEntityMetadataType(18, EntityMetadataType.OptionalLivingEntityReference); writer.WriteOptional(Owner != Guid.Empty ? (Guid?)Owner : null);
        writer.WriteEntityMetadataType(19, EntityMetadataType.Boolean); writer.WriteBoolean(dashTicks > 0);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    { if (Owner != Guid.Empty) writer.WriteArray("Owner", EntityNbt.UuidToInts(Owner)); writer.WriteInt("ObsidianDashCooldown", dashCooldown); }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4) Owner = EntityNbt.UuidFromInts(owner.GetArray());
        dashCooldown = Math.Clamp(tag.TryGetTagValue<int>("ObsidianDashCooldown", out var cooldown) ? cooldown : 0, 0, 40);
    }
}

internal sealed class NautilusFollowOwnerGoal(Nautilus nautilus) : NavigationGoal(nautilus, 1)
{
    private IPlayer? owner;
    public override bool CanUse()
    {
        owner = nautilus.Owner == Guid.Empty ? null : nautilus.Level.GetPlayersInRange(nautilus.Position, 32).FirstOrDefault(player => player.Uuid == nautilus.Owner);
        return owner != null && !nautilus.IsInRange(owner, 8);
    }
    public override bool CanContinue() => owner != null && owner.Level == nautilus.Level && !nautilus.IsInRange(owner, 3);
    public override ValueTask TickAsync() { if (owner != null) MoveTo(owner); return default; }
    public override void Stop() { base.Stop(); owner = null; }
}
