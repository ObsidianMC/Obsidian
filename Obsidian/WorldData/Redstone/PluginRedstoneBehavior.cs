namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The redstone signals of plugin blocks: their behaviours' <see cref="Obsidian.API.Blocks.IBlockBehavior.GetSignal"/> and
/// <see cref="Obsidian.API.Blocks.IBlockBehavior.GetDirectSignal"/>, asked through the level's block updates. Answers no signal for
/// any other block.
/// </summary>
internal sealed class PluginRedstoneBehavior(AbstractLevel level) : IRedstoneBehavior
{
    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        level.BlockUpdates.PluginSignalOf(state, position, direction, direct: false);

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        level.BlockUpdates.PluginSignalOf(state, position, direction, direct: true);
}
