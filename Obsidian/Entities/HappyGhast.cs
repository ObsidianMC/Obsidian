using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:happy_ghast")]
public sealed partial class HappyGhast : FarmAnimal
{
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
        var forward = (Rider.Input.HasFlag(PlayerInput.Forward) ? 1 : 0) - (Rider.Input.HasFlag(PlayerInput.Backward) ? 1 : 0);
        MoveControl.Acceleration = (VectorD)Rider.GetLookDirection() * (forward * 0.025);
        Motion *= 0.8;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    { if (!GetEquipment(EquipmentSlot.Body).IsAir) DropItem(GetEquipment(EquipmentSlot.Body)); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(Rider != null);
    }
}
