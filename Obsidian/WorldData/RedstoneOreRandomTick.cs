using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>RedStoneOreBlock.randomTick</c>: lit redstone ore goes dark. Only lit ore random-ticks, so the dark
/// state never gets here.
/// </summary>
internal sealed class RedstoneOreRandomTick : IRandomTickBehavior
{
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (block.GetProperty("lit") == "true")
            level.BlockUpdates.SetBlock(position, block.WithProperty("lit", false));
    }
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior RedStoneOreBlock { get; } = new RedstoneOreRandomTick();
}
