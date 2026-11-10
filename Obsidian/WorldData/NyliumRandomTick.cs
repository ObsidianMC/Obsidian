using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>NyliumBlock.randomTick</c>: nylium under a block that lets no light through to it turns back into
/// netherrack.
/// </summary>
internal sealed class NyliumRandomTick : IRandomTickBehavior
{
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var blocks = level.BlockUpdates;
        var above = blocks.GetBlock(position + Vector.Up);

        // Vanilla's canBeNylium: the light the block above lets down into the nylium (getLightDampeningInto) is
        // under 15, where faces that close the gap between them block it all.
        var dampening = BlockLight.LightShapesOcclude(block, above, BlockFace.Up) ? 16 : above.LightBlock();
        if (dampening < 15)
            return;

        blocks.SetBlock(position, BlocksRegistry.GetDefault(BlockIds.Netherrack));
    }
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior NyliumBlock { get; } = new NyliumRandomTick();
}
