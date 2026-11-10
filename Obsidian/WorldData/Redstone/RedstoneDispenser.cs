using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Dispensing;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The dispenser's and dropper's block, like vanilla's <c>DispenserBlock</c> and <c>DropperBlock</c>: it faces the
/// player (away from the direction they look closest to), and a rising redstone signal, at it or the block above,
/// fires it four game ticks later. A dropper puts one of its items into the container it faces, or else drops it; a
/// dispenser fires one of its items with the item's dispense behaviour (see <see cref="DispenseBehaviors"/>).
/// </summary>
internal sealed class RedstoneDispenser(IRedstoneLevel level, RedstoneEngine engine, bool dropper) : INeighborChangedBehavior
{
    // Vanilla's delay between a dispenser being triggered and firing.
    private const int FireDelay = 4;

    // Vanilla's LevelEvent.PARTICLES_SHOOT_SMOKE.
    private const int SmokeEvent = 2000;

    public const int DispenserId = BlockIds.Dispenser;
    public const int DropperId = BlockIds.Dropper;

    public static void Register(IRedstoneLevel level, RedstoneEngine engine, LevelBlockTicks ticks, NeighborBehaviors neighbors)
    {
        foreach (var (id, dropper) in new[] { (DispenserId, false), (DropperId, true) })
        {
            var behavior = new RedstoneDispenser(level, engine, dropper);
            neighbors.Register(id, behavior);
            ticks.Register(id, (position, state) => level.FireDispenser(position, state, dropper));
        }
    }

    public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        var position = change.Position;
        var powered = engine.HasNeighborSignal(position) || engine.HasNeighborSignal(position + Vector.Up);
        var triggered = change.State.GetProperty("triggered") == "true";

        if (powered && !triggered)
        {
            level.ScheduleBlockTick(position, change.State, FireDelay);
            await level.WriteAsync(position, change.State.WithProperty("triggered", true), notifyNeighbors: false);
        }
        else if (!powered && triggered)
        {
            await level.WriteAsync(position, change.State.WithProperty("triggered", false), notifyNeighbors: false);
        }
    }

    // Vanilla's dispenseFrom, for droppers and dispensers. Run on the level's entity path, outside its tick lock.
    internal static async ValueTask FireAsync(AbstractLevel level, Vector position, IBlock state, bool dropper)
    {
        var updates = level.BlockUpdates;
        if (!updates.GetBlock(position).IsSameState(state)
            || updates.GetBlockEntity(position) is not ContainerBlockEntity container)
        {
            return;
        }

        if (dropper)
        {
            Drop(level, position, state, container);
            return;
        }

        // Snapshot outside tick: pending loot and placement components may run arbitrary code. Never carry
        // this inventory into a tick-lock wait. The commit below revalidates the exact stack and resident entity.
        int slot;
        long revision;
        ItemStack? original;
        ItemStack? snapshot;
        using (var held = await container.LockInventoryAsync())
        {
            if (held is null)
                return;

            revision = container.Inventory.Revision;
            slot = RandomSlot(container.Inventory, level);
            original = slot < 0 ? null : container.Inventory.GetItem(slot);
            snapshot = original is null ? null : new ItemStack(original, original.Count);
        }

        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        var source = new DispenseSource(level, position, state, facing, container.Inventory);
        using var prepared = snapshot is null ? null : DispenseBehaviors.Prepare(source, snapshot);
        source = source with { Placement = prepared };

        lock (level.Fluids.TickLock)
        {
            if (!updates.GetBlock(position).IsSameState(state)
                || updates.GetBlockEntity(position) != container
                || container.IsRemoved
                || container.LootTable is not null
                || !container.Inventory.TryLock(out var held))
            {
                return;
            }

            BlockUseEffects? effects = null;
            try
            {
                using (held)
                {
                    var inventory = container.Inventory;
                    if (inventory.IsRetired || inventory.Revision != revision || slot >= 0
                        && (!ReferenceEquals(inventory.GetItem(slot), original)
                            || original!.Count != snapshot!.Count || !original.Equals(snapshot)))
                    {
                        return;
                    }

                    using (effects = new BlockUseEffects(level))
                    {
                        if (slot < 0)
                        {
                            level.BroadcastLevelEvent(Dispensed.FailEvent, position, 0);
                            level.EmitGameEvent(MobGameEvent.BlockActivate,
                                (VectorD)position + new VectorD(0.5, 0.5, 0.5), affectedBlock: state);
                        }
                        else
                        {
                            var dispensed = DispenseBehaviors.For(snapshot!).Dispense(source, snapshot!);
                            inventory.SetItem(slot, dispensed.Rest);
                            level.BroadcastLevelEvent(dispensed.Event, position, 0);
                            level.BroadcastLevelEvent(SmokeEvent, position, (int)facing);
                        }
                    }
                }
            }
            finally
            {
                // Notifications and factories never run while the inventory is held, including on failure.
                if (effects is not null)
                {
                    container.SetChanged();
                    container.QueueItemsChanged();
                    effects.Publish();
                }
            }
        }
    }

    // A dropper puts one of its items into the container it faces if it fits, and otherwise keeps it silently; facing
    // anything else it drops the item, like a dispenser's default behaviour.
    private static void Drop(AbstractLevel level, Vector position, IBlock state, ContainerBlockEntity container)
    {
        if (container.GetItemAccess(side: null) is not IItemAccess own)
            return;

        var slot = RandomSlot(own, level);
        if (slot < 0)
        {
            level.BroadcastLevelEvent(Dispensed.FailEvent, position, 0);
            return;
        }

        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        if (level.GetItemAccess(position.Offset(facing), facing.Opposite()) is IItemAccess target)
        {
            Hoppers.TryMoveOne(own, slot, target, out var leftover);
            Hoppers.DropLeftover(level, position, leftover);
            return;
        }

        if (own.Extract(slot, 1) is ItemStack item)
        {
            DispenseBehaviors.DropItem(level, position, facing, item);
            level.BroadcastLevelEvent(Dispensed.DispenseEvent, position, 0);
            level.BroadcastLevelEvent(SmokeEvent, position, (int)facing);
        }
    }

    // Vanilla's DispenserBlockEntity.getRandomSlot: each slot holding something replaces the pick with a chance of one
    // in how many such slots there were so far, drawing from the level's random.
    private static int RandomSlot(IItemAccess own, AbstractLevel level) =>
        RandomSlot(own.SlotCount, slot => own.GetItem(slot) is not null, level);

    private static int RandomSlot(BaseContainer inventory, AbstractLevel level) =>
        RandomSlot(inventory.Size, slot => inventory.GetItem(slot) is { Count: > 0, IsAir: false }, level);

    private static int RandomSlot(int slots, Func<int, bool> holds, AbstractLevel level)
    {
        var picked = -1;
        var seen = 1;
        for (var slot = 0; slot < slots; slot++)
        {
            if (holds(slot) && level.GrowthRandom.NextInt(seen++) == 0)
                picked = slot;
        }

        return picked;
    }
}
