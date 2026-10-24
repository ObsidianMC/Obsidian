using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The dispenser's and dropper's block, like vanilla's <c>DispenserBlock</c> and <c>DropperBlock</c>: it faces the
/// player (away from the direction they look closest to), and a rising redstone signal, at it or the block above,
/// fires it four game ticks later. A dropper puts one of its items into the container it faces, or else drops it; a
/// dispenser drops it.
/// </summary>
/// <remarks>
/// Vanilla's dispensers use many items (shoot arrows, place water, ...); here they place boats and minecarts (see
/// <see cref="VehicleItems.Dispense"/>), and drop every other item, like vanilla's default behaviour.
/// </remarks>
internal sealed class RedstoneDispenser(IRedstoneLevel level, RedstoneEngine engine, bool dropper) : INeighborChangedBehavior
{
    // Vanilla's delay between a dispenser being triggered and firing.
    private const int FireDelay = 4;

    // Vanilla's LevelEvent.SOUND_DISPENSER_DISPENSE, SOUND_DISPENSER_FAIL and PARTICLES_SHOOT_SMOKE.
    private const int DispenseEvent = 1000;
    private const int FailEvent = 1001;
    private const int SmokeEvent = 2000;

    // Vanilla DefaultDispenseItemBehavior: an item's speed (6) times its spread per unit of speed.
    private const double DropSpread = 6 * 0.0172275;

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

    // Vanilla's dispenseFrom.
    internal static ValueTask Fire(AbstractLevel level, Vector position, IBlock state, bool dropper)
    {
        var updates = level.BlockUpdates;
        if (!updates.GetBlock(position).IsSameState(state)
            || updates.GetBlockEntity(position) is not ContainerBlockEntity container
            || container.GetItemAccess(side: null) is not IItemAccess own)
        {
            return ValueTask.CompletedTask;
        }

        var slot = RandomSlot(own, level);
        if (slot < 0)
        {
            level.BroadcastLevelEvent(FailEvent, position, 0);
            return ValueTask.CompletedTask;
        }

        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));

        // A dropper facing a container puts the item in if it fits, and otherwise keeps it, silently.
        if (dropper && level.GetItemAccess(position.Offset(facing), facing.Opposite()) is IItemAccess target)
        {
            Hoppers.TryMoveOne(own, slot, target, out var leftover);
            Hoppers.DropLeftover(level, position, leftover);
            return ValueTask.CompletedTask;
        }

        // A dispenser places boats and minecarts, like vanilla's boat and minecart dispense behaviours.
        if (!dropper
            && own.GetItem(slot) is ItemStack vehicle
            && VehicleItems.Dispense(level, position, facing, vehicle))
        {
            own.Extract(slot, 1);
            level.BroadcastLevelEvent(DispenseEvent, position, 0);
            level.BroadcastLevelEvent(SmokeEvent, position, (int)facing);
            return ValueTask.CompletedTask;
        }

        if (own.Extract(slot, 1) is ItemStack item)
        {
            DropItem(level, position, facing, item);
            level.BroadcastLevelEvent(DispenseEvent, position, 0);
            level.BroadcastLevelEvent(SmokeEvent, position, (int)facing);
        }

        return ValueTask.CompletedTask;
    }

    // Vanilla's DispenserBlockEntity.getRandomSlot: each slot holding something replaces the pick with a chance of one
    // in how many such slots there were so far, drawing from the level's random.
    private static int RandomSlot(IItemAccess own, AbstractLevel level)
    {
        var picked = -1;
        var seen = 1;
        for (var slot = 0; slot < own.SlotCount; slot++)
        {
            if (own.GetItem(slot) is not null && level.GrowthRandom.NextInt(seen++) == 0)
                picked = slot;
        }

        return picked;
    }

    // Vanilla's DefaultDispenseItemBehavior.spawnItem: from 0.7 blocks out of the centre, lowered by a little, with the
    // item entity's own motion drawn and replaced by one along the facing.
    internal static void DropItem(AbstractLevel level, Vector position, BlockFace facing, ItemStack item)
    {
        var step = facing.ToVector();
        var x = position.X + 0.5 + 0.7 * step.X;
        var y = position.Y + 0.5 + 0.7 * step.Y - (facing.IsVertical() ? 0.125 : 0.15625);
        var z = position.Z + 0.5 + 0.7 * step.Z;

        var random = level.GrowthRandom;
        random.NextDouble();
        random.NextDouble();

        var speed = random.NextDouble() * 0.1 + 0.2;
        var motion = new VectorD(
            random.Triangle(step.X * speed, DropSpread),
            random.Triangle(0.2, DropSpread),
            random.Triangle(step.Z * speed, DropSpread));

        level.TrySpawnEntity(new ItemEntity
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(x, y, z),
            Item = item,
            Motion = motion
        }, hostOwned: true);
    }
}
