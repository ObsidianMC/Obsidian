using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for mega/large pine trees - creates a wide conical crown with jagged edges.
/// </summary>
[TreeProperty("minecraft:mega_pine_foliage_placer")]
public sealed class MegaPineFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// Height of the crown/foliage section (0-24 blocks).
    /// </summary>
    [Range(0, 24)]
    public required IIntProvider CrownHeight { get; set; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        return CrownHeight.Get();
    }

    public override int FoliageRadius(Random random, int trunkHeight)
    {
        // Mega pine needs a base radius for the formula to work correctly
        // The actual radius grows with the formula in Place()
        return 1;
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
            bool doubleTrunk = true; // MegaPine uses GiantTrunkPlacer which creates 2x2 trunks
            int radiusOffset = 0; // Can be from attachment

            var foliagePos = attachment;
            int prevRadius = 0;

            // Place layers from bottom to top (yo represents distance from foliagePos)
            for (int yy = foliagePos.Y - foliageHeight + offset; yy <= foliagePos.Y + offset; yy++)
            {
                int yo = foliagePos.Y - yy; // Distance below the attachment point

                // Calculate smooth radius based on vertical position
                // Formula: radius increases as we go up (yo decreases)
                float smoothRadiusCalc = leafRadius + radiusOffset + MathF.Floor((float)yo / foliageHeight * 3.5f);
                int smoothRadius = (int)smoothRadiusCalc;

                // Add jagged variation - every other layer alternates if radius stays the same
                int jaggedRadius;
                if (yo > 0 && smoothRadius == prevRadius && (yy & 1) == 0)
                {
                    jaggedRadius = smoothRadius + 1;
                }
                else
                {
                    jaggedRadius = smoothRadius;
                }

                // Place leaves at this layer
                var layerPos = new Vector(foliagePos.X, yy, foliagePos.Z);
                await FoliagePlacerHelper.PlaceLeavesRow(
                    context.World,
                    random,
                    foliageBlock,
                    layerPos,
                    jaggedRadius,
                    0, // y offset is already in layerPos
                    doubleTrunk,
                    ShouldSkipLocation,
                    placedPositions
                );

                prevRadius = smoothRadius;
            }
        }

        return placedPositions;
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // Skip if too far from center (creates circular shape)
        // Also skip if dx + dz >= 7 (cuts off far corners for mega trees)
        if (dx + dz >= 7)
            return true;

        // Use circular distance check
        return dx * dx + dz * dz > currentRadius * currentRadius;
    }
}
