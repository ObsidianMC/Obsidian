using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:mega_jungle_trunk_placer")]
public sealed class MegaJungleTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var trunkPositions = new List<Vector>();
        var random = context.Random;

        // First, place the base 2x2 giant trunk (same as GiantTrunkPlacer)
        var below = origin + (0, -1, 0);
        await context.World.SetBlockUntrackedAsync(below, BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + (1, 0, 0), BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + (0, 0, 1), BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + (1, 0, 1), BlocksRegistry.Dirt, false);

        // Place 2x2 trunk (don't add to trunk positions - we'll add attachment points separately)
        for (int hh = 0; hh < treeHeight; hh++)
        {
            await PlaceLogIfFree(context, origin, 0, hh, 0, trunkBlock);

            if (hh < treeHeight - 1)
            {
                await PlaceLogIfFree(context, origin, 1, hh, 0, trunkBlock);
                await PlaceLogIfFree(context, origin, 1, hh, 1, trunkBlock);
                await PlaceLogIfFree(context, origin, 0, hh, 1, trunkBlock);
            }
        }

        // Add main trunk foliage attachment point
        trunkPositions.Add(origin + (0, treeHeight, 0));

        // Add branches at various heights
        for (int branchHeight = treeHeight - 2 - random.Next(4); branchHeight > treeHeight / 2; branchHeight -= 2 + random.Next(4))
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            int bx = 0;
            int bz = 0;

            // Place 5 logs extending out in a direction
            for (int b = 0; b < 5; b++)
            {
                bx = (int)(1.5f + Math.Cos(angle) * b);
                bz = (int)(1.5f + Math.Sin(angle) * b);
                // Place branch blocks WITHOUT adding as foliage attachment points
                await PlaceLogIfFree(context, origin, bx, branchHeight - 3 + b / 2, bz, trunkBlock);
            }

            // Add foliage attachment point at the end of the branch
            trunkPositions.Add(origin + (bx, branchHeight, bz));
        }

        return trunkPositions;
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

    private async ValueTask PlaceLogIfFreeAndAddAttachment(
        FeatureContext context,
        Vector treePos,
        int x,
        int y,
        int z,
        IBlock trunkBlock,
        List<Vector> trunkPositions)
    {
        var pos = treePos + (x, y, z);

        // Check if position is replaceable
        var existingBlock = await context.World.GetBlockAsync(pos);
        if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
        {
            await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
            trunkPositions.Add(pos);
        }
    }
}
