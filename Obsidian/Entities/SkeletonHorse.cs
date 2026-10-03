namespace Obsidian.Entities;

[MinecraftEntity("minecraft:skeleton_horse")]
public sealed partial class SkeletonHorse : AbstractHorse
{
    private Skeleton? skeletonRider;
    private Guid skeletonRiderUuid;
    public SkeletonHorse() => Type = EntityType.SkeletonHorse;
    public bool SkeletonTrap { get; internal set; }
    public int TrapTime { get; internal set; }
    protected override bool UsesAi => true;
    protected override string? SoundName => "skeleton_horse";
    protected override void FinalizeSpawn()
    {
        TryUpdateAttribute("minecraft:generic.movement_speed", 0.2f);
        TryUpdateAttribute("minecraft:generic.max_health", 15);
        Health = 15;
    }
    protected override void RegisterGoals(AI.GoalSelector actions, AI.GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new SkeletonHorseRiderGoal(this));
    }
    internal IEntity? RiderTarget => skeletonRider?.AttackTarget;
    internal void AttachSkeleton(Skeleton skeleton)
    {
        skeletonRider = skeleton;
        skeletonRiderUuid = skeleton.Uuid;
        skeleton.HorseVehicle = this;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SetPassengersPacket
        { EntityId = EntityId, Passengers = [skeleton.EntityId] });
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (skeletonRider == null && skeletonRiderUuid != Guid.Empty && AiTick % 20 == 0)
        {
            var rider = Level.GetEntitiesInRange(Position, 64).OfType<Skeleton>().FirstOrDefault(entity => entity.Uuid == skeletonRiderUuid);
            if (rider != null) AttachSkeleton(rider);
        }
        if (skeletonRider is { Alive: false } || skeletonRider?.IsRemoved == true)
        {
            if (skeletonRider != null) skeletonRider.HorseVehicle = null;
            skeletonRider = null;
            skeletonRiderUuid = Guid.Empty;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SetPassengersPacket
            { EntityId = EntityId, Passengers = [] });
        }
        if (!SkeletonTrap) return;
        if (++TrapTime >= 18000) { await RemoveAsync(); return; }
        if (!Level.GetPlayersInRange(Position, 10).Any(player => player.Health > 0 && player.Gamemode != Gamemode.Spectator)) return;
        SkeletonTrap = false;
        HorseMask |= HorseMask.Tamed;
        PersistenceRequired = true;
        SynchronizeMetadata();
        for (var index = 0; index < 4; index++)
        {
            var horse = index == 0 ? this : new SkeletonHorse { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position,
                HorseMask = HorseMask.Tamed, PersistenceRequired = true, Motion = new VectorF((Random.NextSingle() - 0.5f) * 0.3f, 0, (Random.NextSingle() - 0.5f) * 0.3f) };
            if (index > 0) Level.SpawnEntity(horse);
            var skeleton = new Skeleton { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position + new VectorF(0, Dimension.Height, 0), PersistenceRequired = true };
            skeleton.InitializeAi();
            skeleton.SetEquipment(Obsidian.API.Inventory.EquipmentSlot.Helmet, ItemsRegistry.GetSingleItem(Material.IronHelmet));
            skeleton.SetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.Bow));
            Level.SpawnEntity(skeleton);
            horse.AttachSkeleton(skeleton);
        }
    }
    protected override void WriteAdditionalSave(Obsidian.Nbt.Interfaces.INbtWriter writer)
    {
        writer.WriteBool("SkeletonTrap", SkeletonTrap);
        writer.WriteInt("SkeletonTrapTime", TrapTime);
        if (skeletonRiderUuid != Guid.Empty) writer.WriteArray("ObsidianSkeletonRider", EntityNbt.UuidToInts(skeletonRiderUuid));
    }
    protected override void ReadAdditionalSave(Obsidian.Nbt.NbtCompound tag)
    {
        SkeletonTrap = tag.TryGetBool("SkeletonTrap", out var trap) && trap;
        TrapTime = Math.Clamp((tag.TryGetTagValue<int>("SkeletonTrapTime", out var savedSkeletonTrapTime) ? savedSkeletonTrapTime : 0), 0, 18000);
        if (tag.TryGetTag<Obsidian.Nbt.NbtArray<int>>("ObsidianSkeletonRider", out var rider) && rider.Count == 4)
            skeletonRiderUuid = EntityNbt.UuidFromInts(rider.GetArray());
    }
}

internal sealed class SkeletonHorseRiderGoal(SkeletonHorse horse) : AI.NavigationGoal(horse, 1.2f)
{
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => horse.RiderTarget is { Health: > 0 } target && target.Level == horse.Level;
    public override ValueTask TickAsync() { MoveTo(horse.RiderTarget!); return default; }
}
