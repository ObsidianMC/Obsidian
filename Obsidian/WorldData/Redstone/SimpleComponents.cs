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

/// <summary>
/// TNT, like vanilla's <c>TntBlock</c>: powering it, or placing it where it's powered, lights it.
/// </summary>
internal sealed class TntComponent : RedstoneComponent
{
    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
        if (oldState.RegistryId != state.RegistryId && level.HasNeighborSignal(position))
            Prime(level, position);
    }

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (level.HasNeighborSignal(position))
            Prime(level, position);
    }

    // The block goes now; the lit TNT spawns on the level's tick, as entities do.
    private static void Prime(LevelBlockUpdates level, Vector position)
    {
        if (level.Level is not AbstractLevel owner || !owner.LevelData.GetBooleanRule("tnt_explodes"))
            return;

        level.Set(position, BlocksRegistry.Air, BlockUpdateFlags.All);
        owner.EnqueueEntityAction(() =>
        {
            Entities.PrimedTnt.Prime(owner, position, owner: null);
            return ValueTask.CompletedTask;
        });
    }
}
