using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:happy_ghast")]
public sealed partial class HappyGhast : FarmAnimal
{
    private int stillTimeout;
    private VectorD? home;
    protected override int MaximumPassengers => 4;
    protected override VectorD PassengerOffset(int index)
    {
        var angle = (Yaw.Degrees + index * 90) * MathF.PI / 180;
        return new VectorD(-MathF.Sin(angle) * 1.1f, Dimension.Height, MathF.Cos(angle) * 1.1f);
    }
    public HappyGhast() => Type = EntityType.HappyGhast;
    internal override bool FlyingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override float DimensionScale => IsBaby ? 0.3f : 1;
    internal override bool CanBreed => false;
    protected override string? SoundName => "happy_ghast";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0 } && TagsRegistry.Item.HappyGhastFood.Entries.Contains(item.Holder.Id);
    protected override VectorD Travel() => VolumeMovement.Travel(this, true);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new PanicGoal(this, 2));
        actions.AddGoal(1, new TemptGoal(this, item => CanEat(item) || item is { Count: > 0 } && item.Holder.UnlocalizedName.EndsWith("_harness", StringComparison.Ordinal), 1));
        actions.AddGoal(2, new RandomStrollGoal(this, 1));
        actions.AddGoal(3, new LookAtPlayerGoal(this, 8));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        home ??= Position;
        if (Rider != null) home = Position;
        if (stillTimeout > 0) stillTimeout--;
        if (!IsBaby && Level.GetPlayersInRange(Position, 5).Any(player => player is Player { Vehicle: null } &&
            player.Position.Y >= Position.Y + Dimension.Height - 0.2 && player.Position.Y <= Position.Y + Dimension.Height + 1 &&
            Math.Abs(player.Position.X - Position.X) <= Dimension.Width / 2 && Math.Abs(player.Position.Z - Position.Z) <= Dimension.Width / 2))
            stillTimeout = 10;
        if (stillTimeout > 0 && Rider == null) { GoalController?.Pause(); MoveControl.Stop(); Motion = VectorD.Zero; }
        else if (Rider == null && GoalController is GoalSelector { IsPaused: true } goals) goals.Resume();
        var radius = IsBaby || !GetEquipment(EquipmentSlot.Body).IsAir ? 32 : 64;
        if (Rider == null && stillTimeout == 0 && home is { } center && (Position - center).MagnitudeSquared() > radius * radius)
            MoveControl.MoveTo(center, 1);
        var fast = Terrain.IsRainingAt((Vector)Position.Floor()) || Terrain.GetTemperature((Vector)Position.Floor()) < 0.15;
        if (AiTick % (fast ? 20 : 600) == 0) Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
    }
    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (IsBaby && CanEat(held)) { await base.FeedAsync(player, hand); return; }
        if (IsBaby) return;
        if (held is { Count: > 0 } && held.Holder.UnlocalizedName.EndsWith("_harness", StringComparison.Ordinal))
        {
            if (!GetEquipment(EquipmentSlot.Body).IsAir) DropItem(GetEquipment(EquipmentSlot.Body));
            SetEquipment(EquipmentSlot.Body, new ItemStack(held));
            await ConsumeInteractionItemAsync(player, hand);
        }
        else if (held is { Count: > 0, Type: Material.Shears } && !GetEquipment(EquipmentSlot.Body).IsAir && Rider == null)
        {
            DropItem(GetEquipment(EquipmentSlot.Body)); SetEquipment(EquipmentSlot.Body, ItemStack.Air);
            await DamageInteractionToolAsync(player, hand, 1);
        }
        else if (!player.Sneaking && !GetEquipment(EquipmentSlot.Body).IsAir && player is Player rider && Mount(rider)) GoalController?.Pause();
    }
    protected override void TickRidden()
    {
        base.TickRidden();
        if (Rider == null) { if (GoalController is GoalSelector { IsPaused: true } goals) goals.Resume(); return; }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        Yaw = Rider.Yaw;
        Pitch = Rider.Pitch;
        var forward = (Rider.Input.HasFlag(PlayerInput.Forward) ? 1f : 0) - (Rider.Input.HasFlag(PlayerInput.Backward) ? 0.5f : 0);
        var sideways = (Rider.Input.HasFlag(PlayerInput.Left) ? 1f : 0) - (Rider.Input.HasFlag(PlayerInput.Right) ? 1f : 0);
        var yaw = Yaw.Degrees * MathF.PI / 180;
        var input = (VectorD)Rider.GetLookDirection() * forward + new VectorD(MathF.Cos(yaw) * sideways,
            Rider.Input.HasFlag(PlayerInput.Jump) ? 0.5f : 0, MathF.Sin(yaw) * sideways);
        if (input.MagnitudeSquared() > 1) input /= input.Magnitude;
        MoveControl.Acceleration = input * (GetAttributeValue("minecraft:generic.flying_speed") * 0.78f);
        Motion *= 0.8;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    { if (!GetEquipment(EquipmentSlot.Body).IsAir) DropItem(GetEquipment(EquipmentSlot.Body)); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(stillTimeout > 0);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("still_timeout", stillTimeout);
        if (home is { } center) writer.WriteArray("ObsidianHome", new[] { (int)center.X, (int)center.Y, (int)center.Z });
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        stillTimeout = tag.TryGetTagValue<int>("still_timeout", out var timeout) ? Math.Clamp(timeout, 0, 60) : 0;
        if (tag.TryGetTag<NbtArray<int>>("ObsidianHome", out var saved) && saved.Count == 3)
        { var values = saved.GetArray(); home = new VectorD(values[0], values[1], values[2]); }
    }
}
