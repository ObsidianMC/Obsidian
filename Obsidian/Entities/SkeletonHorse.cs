using Obsidian.WorldData;

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
        skeleton.Ride(this, force: true);
    }

    // The trap's skeletons sit lower than a player would.
    protected override VectorD PassengerOffset(int index) => this.Passengers[index] is Skeleton
        ? new VectorD(0, this.Dimension.Height * 0.75f, 0)
        : base.PassengerOffset(index);

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        if (skeletonRider == null && skeletonRiderUuid != Guid.Empty && AiTick % 20 == 0)
        {
            var rider = Level.FindEntityInRange(skeletonRiderUuid, Position, 64) as Skeleton;
            if (rider != null)
                AttachSkeleton(rider);
        }
        if (skeletonRider is { Alive: false } || skeletonRider?.IsRemoved == true)
        {
            skeletonRider?.LeaveVehicle();
            skeletonRider = null;
            skeletonRiderUuid = Guid.Empty;
        }
        if (!SkeletonTrap)
            return;
        if (++TrapTime >= 18000)
        {
            await RemoveAsync();
            return;
        }
        if (!Level.GetPlayersInRange(Position, 10).Any(player => player.Health > 0 && player.GameMode != GameMode.Spectator))
            return;
        SkeletonTrap = false;
        HorseMask |= HorseMask.Tamed;
        PersistenceRequired = true;
        SynchronizeMetadata();
        for (var index = 0; index < 4; index++)
        {
            var horse = index == 0 ? this : new SkeletonHorse
            {
                Level = Level,
                EntityId = Server.GetNextEntityId(),
                Position = Position,
                HorseMask = HorseMask.Tamed,
                PersistenceRequired = true,
                Motion = new VectorD((Random.NextSingle() - 0.5f) * 0.3f, 0, (Random.NextSingle() - 0.5f) * 0.3f)
            };
            if (index > 0)
                Level.SpawnEntity(horse);
            var skeleton = new Skeleton { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position + new VectorD(0, Dimension.Height, 0), PersistenceRequired = true };
            skeleton.InitializeAi();
            skeleton.SetEquipment(
                Obsidian.API.Inventory.EquipmentSlot.Helmet,
                ItemsRegistry.GetSingleItem(ItemsRegistry.IronHelmet));
            skeleton.SetEquipment(
                Obsidian.API.Inventory.EquipmentSlot.MainHand,
                ItemsRegistry.GetSingleItem(ItemsRegistry.Bow));
            Level.SpawnEntity(skeleton);
            horse.AttachSkeleton(skeleton);
        }
    }

    protected override void WriteAdditionalSave(Obsidian.Nbt.Interfaces.INbtWriter writer)
    {
        writer.WriteBool("SkeletonTrap", SkeletonTrap);
        writer.WriteInt("SkeletonTrapTime", TrapTime);
        if (skeletonRiderUuid != Guid.Empty)
            writer.WriteArray("ObsidianSkeletonRider", EntityNbt.UuidToInts(skeletonRiderUuid));
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

    public override ValueTask TickAsync()
    {
        MoveTo(horse.RiderTarget!);
        return default;
}
}
