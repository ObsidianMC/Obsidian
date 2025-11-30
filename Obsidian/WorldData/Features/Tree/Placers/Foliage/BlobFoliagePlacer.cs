using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for standard rounded blob-shaped canopies (oak, birch, etc).
/// Creates a spherical canopy with radius that decreases going up.
/// </summary>
[TreeProperty("minecraft:blob_foliage_placer")]
public sealed class BlobFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// Height of the blob foliage (0-16 blocks).
    /// </summary>
    [Range(0, 16)]
    public required int Height { get; init; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        return Height;
    }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock)
    {
        var random = context.Random;
        var placedPositions = new List<Vector>();

        foreach (var attachment in trunkPositions)
        {
            int leafRadius = FoliageRadius(random, treeHeight);
            int offset = GetOffset(random);
            int foliageHeight = GetFoliageHeight(random, treeHeight);
            bool doubleTrunk = false; // Most blob trees are single trunk
            int radiusOffset = 0; // Can be from attachment

            var foliagePos = attachment;

            // Place layers from top (offset) down to (offset - foliageHeight)
            // Each layer has a decreasing radius as we go up
            for (int yo = offset; yo >= offset - foliageHeight; yo--)
            {
                // Calculate radius for this layer
                // Formula: radius decreases by 1 for every 2 layers going up
                // Max ensures we never go below radius 0
                int currentRadius = Math.Max(leafRadius + radiusOffset - 1 - yo / 2, 0);

                await FoliagePlacerHelper.PlaceLeavesRow(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    currentRadius,
                    yo,
                    doubleTrunk,
                    ShouldSkipLocation,
                    placedPositions
                );
            }
        }

        return placedPositions;
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // Skip corners with some randomness
        // Always skip corners on the bottom layer (y == 0) or randomly on other layers
        return dx == currentRadius && dz == currentRadius && (random.Next(2) == 0 || y == 0);
    }
}
