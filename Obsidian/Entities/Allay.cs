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
    private Vector? jukebox;
    private Vector? likedNoteBlock;
    private int noteBlockTicks;
    private int throwCooldown;

    internal bool AcceptsVibration(MobVibration vibration) => !MobBitMask.HasFlag(MobBitmask.NoAi) &&
        vibration.Kind == MobGameEvent.NoteBlockPlay && (likedNoteBlock == null || likedNoteBlock == (Vector)vibration.Position.Floor());

    internal void ReceiveVibration(MobVibration vibration)
    {
        if (!AcceptsVibration(vibration)) return;
        likedNoteBlock = (Vector)vibration.Position.Floor();
        noteBlockTicks = 600;
    }

    internal void ReceiveJukeboxEvent(Vector point, bool playing)
    {
        if (playing && (jukebox == null || jukebox == point)) jukebox = point;
        else if (!playing && jukebox == point) jukebox = null;
        var next = jukebox != null;
        if (dancing != next) { dancing = next; SynchronizeMetadata(); }
    }
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
            SetEquipment(EquipmentSlot.MainHand, new ItemStack(item, 1));
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
            throwCooldown = 60;
        }
    }
    protected override async ValueTask TickMobAsync()
    {
        if (duplicationCooldown > 0 && --duplicationCooldown == 0) SynchronizeMetadata();
        if (throwCooldown > 0) throwCooldown--;
        if (noteBlockTicks > 0 && --noteBlockTicks == 0) likedNoteBlock = null;
        if (AiTick % 10 == 0) Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (AiTick % 20 == 0 && jukebox is { } musicPosition)
        {
            if (((VectorD)musicPosition + new VectorD(0.5, 0.5, 0.5) - Position).MagnitudeSquared() > 100 || Terrain.GetBlock(musicPosition)?.Material != Material.Jukebox)
                ReceiveJukeboxEvent(musicPosition, false);
        }
        var held = GetEquipment(EquipmentSlot.MainHand);
        var owner = Level.GetPlayersInRange(Position, 64).FirstOrDefault(player => player.Uuid == likedPlayer && player.Health > 0);
        if (held.IsAir || dancing || throwCooldown > 0 || owner == null && likedNoteBlock == null) return;
        var item = GetEntitiesNear(32).OfType<ItemEntity>().Where(entity => Level.LevelData.GetBooleanRule("mob_griefing") && entity.CanPickup && entity.Item == held &&
            (collected.IsAir || collected.Count < collected.MaxStackSize)).MinBy(entity => (entity.Position - Position).MagnitudeSquared());
        var delivery = likedNoteBlock is { } note ? (VectorD)note + new VectorD(0.5, 1, 0.5) : owner!.Position + new VectorD(0, 1, 0);
        var destination = item != null && (collected.IsAir || collected.Count < collected.MaxStackSize) ? item.Position : delivery;
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
        if (!collected.IsAir && (delivery - Position).MagnitudeSquared() <= 9)
        {
            var stack = new ItemStack(collected, 1);
            var delta = delivery - Position;
            var thrown = new ItemEntity { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position + new VectorD(0, Dimension.Height * 0.6f, 0),
                Item = stack, Motion = delta.Magnitude > 0 ? delta / delta.Magnitude * 0.2 + new VectorD(0, 0.2, 0) : new VectorD(0, 0.2, 0), CanPickup = false };
            Level.SpawnEntity(thrown);
            if (--collected.Count == 0) collected = ItemStack.Air;
            throwCooldown = 20;
        }
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
        writer.WriteInt("ObsidianNoteBlockTicks", noteBlockTicks);
        writer.WriteInt("ObsidianThrowCooldown", throwCooldown);
        if (likedNoteBlock is { } note) writer.WriteArray("ObsidianNoteBlock", new[] { note.X, note.Y, note.Z });
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        duplicationCooldown = Math.Max(0, tag.TryGetTagValue<long>("DuplicationCooldown", out var ticks) ? ticks : 0);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianLikedPlayer", out var owner) && owner.Count == 4) likedPlayer = EntityNbt.UuidFromInts(owner.GetArray());
        if (tag.TryGetTag<NbtCompound>("ObsidianCollected", out var item)) collected = item.ItemFromNbt() ?? ItemStack.Air;
        noteBlockTicks = tag.TryGetTagValue<int>("ObsidianNoteBlockTicks", out var notes) ? Math.Clamp(notes, 0, 600) : 0;
        throwCooldown = tag.TryGetTagValue<int>("ObsidianThrowCooldown", out var throwing) ? Math.Clamp(throwing, 0, 60) : 0;
        if (noteBlockTicks > 0 && tag.TryGetTag<NbtArray<int>>("ObsidianNoteBlock", out var note) && note.Count == 3)
        { var point = note.GetArray(); likedNoteBlock = new Vector(point[0], point[1], point[2]); }
    }
    internal bool Delivering => likedPlayer != Guid.Empty && !dancing && !GetEquipment(EquipmentSlot.MainHand).IsAir;
}
internal sealed class AllayDeliveryGoal(Allay allay) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move;
    public override bool CanUse() => allay.Delivering;
    public override void Start() => ((Navigator)allay.Navigator!).Stop();
}
