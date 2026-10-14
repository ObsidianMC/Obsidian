using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The redstone lamp, like vanilla's <c>RedstoneLampBlock</c>: lit at once when powered, and unlit four game ticks
/// after it no longer is.
/// </summary>
internal sealed class RedstoneLamp : RedstoneComponent
{
    // Vanilla's delay before a lamp goes out.
    private const int OffDelay = 4;

    public override IBlock? GetStateForPlacement(RedstonePlacement placement) =>
        placement.Block.WithProperty("lit", placement.Level.HasNeighborSignal(placement.Position));

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var lit = IsLit(state);
        if (lit == level.HasNeighborSignal(position))
            return;

        if (lit)
            level.ScheduleTick(position, state, OffDelay);
        else
            level.Set(position, state.WithProperty("lit", true), BlockUpdateFlags.Clients);
    }

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (IsLit(state) && !level.HasNeighborSignal(position))
            level.Set(position, state.WithProperty("lit", false), BlockUpdateFlags.Clients);
    }

    private static bool IsLit(IBlock state) => state.GetProperty("lit") == "true";
}

/// <summary>
/// The block of redstone, like vanilla's <c>PoweredBlock</c>: it powers every side, weakly.
/// </summary>
internal sealed class RedstoneBlock : RedstoneComponent
{
    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) => 15;
}
