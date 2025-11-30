using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for bush trees - low ground-level foliage similar to blob but with different radius calculation.
/// </summary>
[TreeProperty("minecraft:bush_foliage_placer")]
public sealed class BushFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// Height of the bush foliage (0-16 blocks).
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
            bool doubleTrunk = false; // Bush trees are single trunk
            int radiusOffset = 0;

            var foliagePos = attachment;

            // Bush places leaves from offset down to offset - foliageHeight
            for (int yo = offset; yo >= offset - foliageHeight; yo--)
            {
                // Bush uses: leafRadius + radiusOffset - 1 - yo
                int currentRadius = leafRadius + radiusOffset - 1 - yo;
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
        // Bush: skip corners randomly (dx == radius AND dz == radius AND 50% chance)
        return dx == currentRadius && dz == currentRadius && random.Next(2) == 0;
    }
}
