using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for regular jungle trees - creates a rounded tropical canopy.
/// </summary>
[TreeProperty("minecraft:jungle_foliage_placer")]
public sealed class JungleFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// Height of the jungle foliage (0-16 blocks).
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
            bool doubleTrunk = false; // Regular jungle trees are single trunk
            int radiusOffset = 0;

            var foliagePos = attachment;

            // Jungle trees place leaves from offset down to offset - foliageHeight
            for (int yo = offset; yo >= offset - foliageHeight; yo--)
            {
                // Jungle: radius decreases as we go up (similar to blob but different formula)
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
        // Jungle: skip corners with probability, always skip on bottom layer (y == 0)
        return dx == currentRadius && dz == currentRadius && (random.Next(2) == 0 || y == 0);
    }
}
