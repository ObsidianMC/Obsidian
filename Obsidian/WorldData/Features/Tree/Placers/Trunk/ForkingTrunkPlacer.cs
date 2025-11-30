using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:forking_trunk_placer")]
public sealed class ForkingTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var trunkPositions = new List<Vector>();
        var random = context.Random;

        // Set dirt at the block below the origin
        var belowOrigin = origin + (0, -1, 0);
        await context.World.SetBlockUntrackedAsync(belowOrigin, BlocksRegistry.Dirt, false);

        // Choose random horizontal direction for the main lean
        var leanDirection = GetRandomHorizontalDirection(random);
        int leanHeight = treeHeight - random.Next(4) - 1;
        int leanSteps = 3 - random.Next(3);

        // Place main trunk with lean
        int tx = (int)origin.X;
        int tz = (int)origin.Z;
        Vector? topPosition = null;

        for (int yo = 0; yo < treeHeight; yo++)
        {
            int yy = (int)origin.Y + yo;

            // Start leaning at leanHeight
            if (yo >= leanHeight && leanSteps > 0)
            {
                tx += leanDirection.X;
                tz += leanDirection.Z;
                leanSteps--;
            }

            var pos = new Vector(tx, yy, tz);
            var existingBlock = await context.World.GetBlockAsync(pos);

            // Only place if the position is replaceable
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
                topPosition = new Vector(tx, yy + 1, tz);
            }
        }

        // Add foliage attachment point for main trunk
        if (topPosition.HasValue)
        {
            trunkPositions.Add(topPosition.Value);
        }

        // Place branch in different direction
        tx = (int)origin.X;
        tz = (int)origin.Z;
        var branchDirection = GetRandomHorizontalDirection(random);

        // Ensure branch goes in different direction than main lean
        if (branchDirection != leanDirection)
        {
            int branchPos = leanHeight - random.Next(2) - 1;
            int branchSteps = 1 + random.Next(3);
            Vector? branchTop = null;

            for (int yo = branchPos; yo < treeHeight && branchSteps > 0; branchSteps--)
            {
                if (yo >= 1)
                {
                    int yyx = (int)origin.Y + yo;
                    tx += branchDirection.X;
                    tz += branchDirection.Z;

                    var pos = new Vector(tx, yyx, tz);
                    var existingBlock = await context.World.GetBlockAsync(pos);

                    // Only place if the position is replaceable
                    if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
                    {
                        await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
                        branchTop = new Vector(tx, yyx + 1, tz);
                    }
                }

                yo++;
            }

            // Add foliage attachment point for branch
            if (branchTop.HasValue)
            {
                trunkPositions.Add(branchTop.Value);
            }
        }

        // Return ONLY foliage attachment points, not all trunk blocks
        return trunkPositions;
    }

    private static Vector GetRandomHorizontalDirection(Random random)
    {
        // Return one of the four cardinal directions using Vector.CardinalDirs
        var cardinals = Vector.CardinalDirs.ToArray();
        return cardinals[random.Next(cardinals.Length)];
    }
}
