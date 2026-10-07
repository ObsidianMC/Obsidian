using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:allay")]
public sealed partial class Allay : PathfinderMob
{
    private Guid likedPlayer;
    private ItemStack collected = ItemStack.Air;
    private long duplicationCooldown;
    private bool dancing;
    public Allay() => Type = EntityType.Allay;
    protected override bool UsesAi => true;
    internal override bool FlyingNavigation => true;
    protected override bool CanDespawn => false;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "allay";
    protected override VectorD Travel() => VolumeMovement.Travel(this, true);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(2, new RandomStrollGoal(this, 1));
        actions.AddGoal(1, new AllayDeliveryGoal(this));
        actions.AddGoal(3, new LookAtPlayerGoal(this, 6));
    }
    public override ValueTask DamageAsync(IEntity source, float amount = 1) => source.Uuid == likedPlayer ? default : base.DamageAsync(source, amount);
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (dancing && duplicationCooldown == 0 && item is { Count: > 0, Type: Material.AmethystShard })
        {
            await ConsumeInteractionItemAsync(player, hand);
            var child = (Allay)Level.GetNewEntitySpawner().WithEntityType(Type).AtPosition(Position).Spawn();
            duplicationCooldown = child.duplicationCooldown = 6000;
            SendEntityEvent(18);
            SynchronizeMetadata();
            child.SynchronizeMetadata();
        }
        else if (GetEquipment(EquipmentSlot.MainHand).IsAir && item is { Count: > 0 })
        {
            SetEquipment(EquipmentSlot.MainHand, new ItemStack(item));
            likedPlayer = player.Uuid;
            PersistenceRequired = true;
            await ConsumeInteractionItemAsync(player, hand);
        }
        else if (!GetEquipment(EquipmentSlot.MainHand).IsAir && (item == null || item.IsAir))
        {
            DropItem(GetEquipment(EquipmentSlot.MainHand));
            if (!collected.IsAir) DropItem(collected);
            SetEquipment(EquipmentSlot.MainHand, ItemStack.Air);
            collected = ItemStack.Air;
            likedPlayer = Guid.Empty;
        }
    }
    protected override async ValueTask TickMobAsync()
    {
        if (duplicationCooldown > 0 && --duplicationCooldown == 0) SynchronizeMetadata();
        if (AiTick % 10 == 0) Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (AiTick % 20 == 0 && Level is AbstractLevel level)
        {
            var origin = (Vector)Position.Floor();
            var music = false;
            for (var x = -10; x <= 10 && !music; x++)
            for (var y = -3; y <= 3 && !music; y++)
            for (var z = -10; z <= 10 && !music; z++)
            {
                var point = origin + new Vector(x, y, z);
                if (Terrain.GetBlock(point)?.Material == Material.Jukebox && level.GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBlockEntity(point.X, point.Y, point.Z) is DataBlockEntity jukebox)
                    music = jukebox.Data.HasTag("RecordItem");
            }
            if (dancing != music) { dancing = music; SynchronizeMetadata(); }
        }
        var held = GetEquipment(EquipmentSlot.MainHand);
        var owner = Level.GetPlayersInRange(Position, 64).FirstOrDefault(player => player.Uuid == likedPlayer && player.Health > 0);
        if (held.IsAir || owner == null || dancing) return;
        var item = GetEntitiesNear(32).OfType<ItemEntity>().Where(entity => entity.CanPickup && entity.Item == held &&
            (collected.IsAir || collected.Count < collected.MaxStackSize)).MinBy(entity => (entity.Position - Position).MagnitudeSquared());
        var destination = item != null && (collected.IsAir || collected.Count < collected.MaxStackSize) ? item.Position : owner.Position + new VectorD(0, 1, 0);
        ((Navigator)Navigator!).Stop();
        MoveControl.MoveTo(destination, 1);
        if (item != null && IsInRange(item, 1.5f))
        {
            var amount = Math.Min(item.Item.Count, collected.IsAir ? held.MaxStackSize : collected.MaxStackSize - collected.Count);
            if (collected.IsAir) collected = new ItemStack(item.Item, amount);
            else collected.Count += amount;
            item.Item.Count -= amount;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TakeItemEntityPacket { CollectedEntityId = item.EntityId, CollectorEntityId = EntityId, PickupItemCount = amount });
            if (item.Item.Count == 0) await item.RemoveAsync();
            else PacketBroadcaster.QueuePacketToLevelInRange(Level, item.Position, new SetEntityDataPacket { EntityId = item.EntityId, Entity = item });
        }
        if (!collected.IsAir && IsInRange(owner, 3)) { DropItem(collected); collected = ItemStack.Air; }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!GetEquipment(EquipmentSlot.MainHand).IsAir) DropItem(GetEquipment(EquipmentSlot.MainHand));
        if (!collected.IsAir) DropItem(collected);
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean); writer.WriteBoolean(dancing);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(duplicationCooldown == 0);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteLong("DuplicationCooldown", duplicationCooldown);
        writer.WriteArray("ObsidianLikedPlayer", EntityNbt.UuidToInts(likedPlayer));
        if (!collected.IsAir) writer.WriteTag(collected.ToNbt("ObsidianCollected"));
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        duplicationCooldown = Math.Max(0, tag.TryGetTagValue<long>("DuplicationCooldown", out var ticks) ? ticks : 0);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianLikedPlayer", out var owner) && owner.Count == 4) likedPlayer = EntityNbt.UuidFromInts(owner.GetArray());
        if (tag.TryGetTag<NbtCompound>("ObsidianCollected", out var item)) collected = item.ItemFromNbt() ?? ItemStack.Air;
    }
    internal bool Delivering => likedPlayer != Guid.Empty && !dancing && !GetEquipment(EquipmentSlot.MainHand).IsAir;
}
internal sealed class AllayDeliveryGoal(Allay allay) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move;
    public override bool CanUse() => allay.Delivering;
    public override void Start() => ((Navigator)allay.Navigator!).Stop();
}
