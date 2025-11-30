using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for acacia trees - creates a flat, umbrella-like canopy.
/// </summary>
[TreeProperty("minecraft:acacia_foliage_placer")]
public sealed class AcaciaFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        // Acacia foliage doesn't add extra height
        return 0;
    }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock)
    {
        var random = context.Random;
        var placedPositions = new List<Vector>();

        foreach (var attachment in trunkPositions)
        {
            // Get radius for this attachment point
            int leafRadius = FoliageRadius(random, treeHeight);
            int offset = GetOffset(random);
            int foliageHeight = GetFoliageHeight(random, treeHeight);

            // Determine if this is a double trunk (2x2)
            bool doubleTrunk = false; // Acacias are typically single trunk

            // Calculate the foliage position with offset
            var foliagePos = attachment + new Vector(0, offset, 0);

            // Acacia has three distinct layers:
            // 1. Bottom layer: larger radius, 1-2 blocks below top
            int radiusOffset = 0; // Can be adjusted based on attachment data
            await FoliagePlacerHelper.PlaceLeavesRow(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                leafRadius + radiusOffset,
                -1 - foliageHeight,
                doubleTrunk,
                ShouldSkipLocation,
                placedPositions
            );

            // 2. Middle layer: smaller radius, 1 block below top
            await FoliagePlacerHelper.PlaceLeavesRow(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                leafRadius - 1,
                -foliageHeight,
                doubleTrunk,
                ShouldSkipLocation,
                placedPositions
            );

            // 3. Top layer: larger radius again, at the top
            await FoliagePlacerHelper.PlaceLeavesRow(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                leafRadius + radiusOffset - 1,
                0,
                doubleTrunk,
                ShouldSkipLocation,
                placedPositions
            );
        }

        return placedPositions;
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // For the top layer (y == 0):
        // Skip corners and positions more than 1 block away if both dx and dz are non-zero
        if (y == 0)
        {
            return (dx > 1 || dz > 1) && dx != 0 && dz != 0;
        }

        // For lower layers:
        // Skip only the outermost corners
        return dx == currentRadius && dz == currentRadius && currentRadius > 0;
    }
}
