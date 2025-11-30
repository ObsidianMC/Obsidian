using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:straight_trunk_placer")]
public sealed class StraightTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        // Set dirt at the block below the origin (like setDirtAt in Java)
        var belowOrigin = origin + (0, -1, 0);
        await context.World.SetBlockUntrackedAsync(belowOrigin, BlocksRegistry.Dirt, false);

        // Place vertical trunk logs
        for (int y = 0; y < treeHeight; y++)
        {
            var pos = origin + (0, y, 0);
            var existingBlock = await context.World.GetBlockAsync(pos);

            // Only place if the position is replaceable (air, grass, etc.)
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
            }
        }

        // Return ONLY the foliage attachment point (origin.above(treeHeight) in Java)
        // Trunk placers should return foliage attachment positions, not all trunk blocks
        return [origin + (0, treeHeight, 0)];
    }
}
