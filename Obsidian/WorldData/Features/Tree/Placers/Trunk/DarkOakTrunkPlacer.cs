using Obsidian.API.Utilities;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:dark_oak_trunk_placer")]
public sealed class DarkOakTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var trunkPositions = new List<Vector>();
        var random = context.Random;

        // Set dirt below the 2x2 origin
        var below = origin + Vector.Down;
        await context.World.SetBlockUntrackedAsync(below, BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + Vector.East, BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + Vector.South, BlocksRegistry.Dirt, false);
        await context.World.SetBlockUntrackedAsync(below + Vector.South + Vector.East, BlocksRegistry.Dirt, false);

        // Determine lean direction and parameters
        var cardinalDirs = Vector.CardinalDirs.ToArray();
        var leanDirection = cardinalDirs[random.Next(cardinalDirs.Length)];
        int leanHeight = treeHeight - random.Next(4);
        int leanSteps = 2 - random.Next(3);

        int x = origin.X;
        int y = origin.Y;
        int z = origin.Z;
        int tx = x;
        int tz = z;
        int ey = y + treeHeight - 1;

        // Place main 2x2 trunk with potential lean
        for (int dy = 0; dy < treeHeight; dy++)
        {
            if (dy >= leanHeight && leanSteps > 0)
            {
                tx += leanDirection.X;
                tz += leanDirection.Z;
                leanSteps--;
            }

            int yy = y + dy;
            var blockPos = new Vector(tx, yy, tz);

            // Place 2x2 logs at this height
            await PlaceLogIfFree(context, blockPos, trunkBlock);
            await PlaceLogIfFree(context, blockPos + Vector.East, trunkBlock);
            await PlaceLogIfFree(context, blockPos + Vector.South, trunkBlock);
            await PlaceLogIfFree(context, blockPos + Vector.South + Vector.East, trunkBlock);
        }

        // Add main trunk top attachment point
        trunkPositions.Add(new Vector(tx, ey, tz));

        // Place additional branch attachments around the perimeter
        for (int ox = -1; ox <= 2; ox++)
        {
            for (int oz = -1; oz <= 2; oz++)
            {
                // Only place branches on the perimeter (not the center 2x2)
                if ((ox < 0 || ox > 1 || oz < 0 || oz > 1) && random.Next(3) <= 0)
                {
                    int length = random.Next(3) + 2;

                    // Place downward branch logs
                    for (int branchY = 0; branchY < length; branchY++)
                    {
                        var branchPos = new Vector(x + ox, ey - branchY - 1, z + oz);
                        await PlaceLogIfFree(context, branchPos, trunkBlock);
                    }

                    // Add branch attachment point for foliage
                    trunkPositions.Add(new Vector(x + ox, ey, z + oz));
                }
            }
        }

        return trunkPositions;
    }

    private async ValueTask PlaceLogIfFree(FeatureContext context, Vector pos, IBlock trunkBlock)
    {
        var existingBlock = await context.World.GetBlockAsync(pos);
        if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
        {
            await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
        }
    }
}
