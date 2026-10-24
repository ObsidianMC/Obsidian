using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Registers every redstone behavior that reacts to neighbors or gives signal. A level and its tests call this once, so both use the
/// same behavior set.
/// </summary>
internal sealed record RedstoneInstall(RedstoneEngine Engine, TripWire Tripwires, TargetBlock Targets);

internal static class RedstoneRegistration
{
    /// <summary>
    /// Creates the engine over <paramref name="world"/> and installs the behaviors. Returns the engine.
    /// </summary>
    public static RedstoneInstall Install(IRedstoneLevel world, LevelBlockTicks ticks, NeighborBehaviors neighbors, RedstoneBehaviors redstone, Action<Vector>? playNote = null)
    {
        var engine = new RedstoneEngine(world, redstone);
        RedstoneWire.Register(redstone, neighbors, engine);
        SculkSensorSignal.Register(redstone);
        BasicRedstoneComponents.Register(world, ticks, redstone, neighbors, engine, playNote);
        RedstoneRepeater.Register(world, ticks, redstone, neighbors, engine);
        RedstoneObserver.Register(world, ticks, redstone, neighbors);
        RedstoneComparator.Register(world, ticks, redstone, neighbors, engine);
        TntIgnition.Register(world, neighbors, engine);
        RedstoneLectern.Register(world, ticks, redstone);
        RedstoneCrafter.Register(world, ticks, neighbors, engine);
        RedstoneDispenser.Register(world, engine, ticks, neighbors);
        RedstoneHopper.Register(world, neighbors, engine);
        var tripwires = TripWire.Register(world, ticks, redstone, neighbors);
        var targets = TargetBlock.Register(world, ticks, redstone);
        return new RedstoneInstall(engine, tripwires, targets);
    }
}
