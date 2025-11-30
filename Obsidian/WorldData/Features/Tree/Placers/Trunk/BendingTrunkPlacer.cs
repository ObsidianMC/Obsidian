using Obsidian.API.Utilities;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:bending_trunk_placer")]
public sealed class BendingTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    [Range(1, 64)]
    public required IIntProvider BendLength { get; init; }

    public int MinHeightForLeaves { get; init; } = 1;

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var random = context.Random;
        var trunkPositions = new List<Vector>();

        // Pick random horizontal direction
        var cardinalDirs = Vector.CardinalDirs.ToArray();
        var direction = cardinalDirs[random.Next(cardinalDirs.Length)];

        int logHeight = treeHeight - 1;
        var pos = origin;
        var belowPos = pos + Vector.Down;

        // Set dirt below origin
        await context.World.SetBlockUntrackedAsync(belowPos, BlocksRegistry.Dirt, false);

        // Place vertical trunk with potential lean at top
        for (int i = 0; i <= logHeight; i++)
        {
            // Start moving horizontally near the top
            if (i + 1 >= logHeight + random.Next(2))
            {
                pos += direction;
            }

            var existingBlock = await context.World.GetBlockAsync(pos);
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
            }

            // Add foliage attachment points starting at MinHeightForLeaves
            if (i >= MinHeightForLeaves)
            {
                trunkPositions.Add(pos);
            }

            pos += Vector.Up;
        }

        // Extend horizontally in the chosen direction
        int dirLength = BendLength.Get();

        for (int i = 0; i <= dirLength; i++)
        {
            var existingBlock = await context.World.GetBlockAsync(pos);
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
            }

            trunkPositions.Add(pos);
            pos += direction;
        }

        return trunkPositions;
    }
}
