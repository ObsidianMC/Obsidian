using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Redstone;

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
