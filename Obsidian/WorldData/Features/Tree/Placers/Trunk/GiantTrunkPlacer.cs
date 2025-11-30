using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:giant_trunk_placer")]
public sealed class GiantTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var trunkPositions = new List<Vector>();

        // Set dirt at the 2x2 base
        var below = origin + (0, -1, 0);
        await context.World.SetBlockUntrackedAsync(below, BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + (1, 0, 0), BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + (0, 0, 1), BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + (1, 0, 1), BlocksRegistry.Dirt, false);

        // Place 2x2 trunk (don't add to trunk positions - we'll add attachment point separately)
        for (int hh = 0; hh < treeHeight; hh++)
        {
            // Place all four logs at this height
            await PlaceLogIfFree(context, origin, 0, hh, 0, trunkBlock);

            // Only place the other 3 logs if not at the top
            if (hh < treeHeight - 1)
            {
                await PlaceLogIfFree(context, origin, 1, hh, 0, trunkBlock);
                await PlaceLogIfFree(context, origin, 1, hh, 1, trunkBlock);
                await PlaceLogIfFree(context, origin, 0, hh, 1, trunkBlock);
            }
        }

        // Return ONLY the foliage attachment point at the top center
        return [origin + (0, treeHeight, 0)];
    }

    private async ValueTask PlaceLogIfFree(
        FeatureContext context,
        Vector treePos,
        int x,
        int y,
        int z,
        IBlock trunkBlock)
    {
        var pos = treePos + (x, y, z);

        // Check if position is replaceable
        var existingBlock = await context.World.GetBlockAsync(pos);
        if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
        {
            await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
        }
    }
}
