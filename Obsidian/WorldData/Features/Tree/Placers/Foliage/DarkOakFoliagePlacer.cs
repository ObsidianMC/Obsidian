using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for dark oak trees - creates a rounded, dense canopy.
/// Dark oaks always have 2x2 trunks and a characteristic thick canopy.
/// </summary>
[TreeProperty("minecraft:dark_oak_foliage_placer")]
public sealed class DarkOakFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        // Dark oak foliage is always 4 blocks tall
        return 4;
    }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock)
    {
        var random = context.Random;
        var placedPositions = new List<Vector>();

        foreach (var attachment in trunkPositions)
        {
            int leafRadius = FoliageRadius(random, treeHeight);
            int offset = GetOffset(random);

            var foliagePos = attachment + new Vector(0, offset, 0);

            // Determine if this is a double trunk (2x2) - dark oaks are always 2x2
            // We can infer this from the tree type, but for now assume true for dark oak
            bool doubleTrunk = true;

            if (doubleTrunk)
            {
                // For 2x2 trunks (standard dark oak):
                // Layer at y=-1: radius + 2
                await PlaceLeavesRowDarkOak(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    leafRadius + 2,
                    -1,
                    doubleTrunk,
                placedPositions
                );

                // Layer at y=0: radius + 3 (widest layer)
                await PlaceLeavesRowDarkOak(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    leafRadius + 3,
                    0,
                    doubleTrunk,
                placedPositions
                );

                // Layer at y=1: radius + 2
                await PlaceLeavesRowDarkOak(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    leafRadius + 2,
                    1,
                    doubleTrunk,
                placedPositions
                );

                // Layer at y=2: base radius (50% chance)
                if (random.Next(2) == 0)
                {
                    await PlaceLeavesRowDarkOak(
                        context.World,
                        random,
                        foliageBlock,
                        foliagePos,
                        leafRadius,
                        2,
                        doubleTrunk,
                    placedPositions
                );
                }
            }
            else
            {
                // For single trunk (rare/alternate variant):
                // Layer at y=-1: radius + 2
                await PlaceLeavesRowDarkOak(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    leafRadius + 2,
                    -1,
                    doubleTrunk,
                placedPositions
                );

                // Layer at y=0: radius + 1
                await PlaceLeavesRowDarkOak(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    leafRadius + 1,
                    0,
                    doubleTrunk,
                placedPositions
                );
            }
        }

        return placedPositions;
    }

    private async ValueTask PlaceLeavesRowDarkOak(
        IWorld world,
        Random random,
        IBlock foliageBlock,
        Vector origin,
        int currentRadius,
        int yOffset,
        bool doubleTrunk,
        List<Vector> placedPositions)
    {
        int offset = doubleTrunk ? 1 : 0;

        for (int dx = -currentRadius; dx <= currentRadius + offset; dx++)
        {
            for (int dz = -currentRadius; dz <= currentRadius + offset; dz++)
            {
                if (!ShouldSkipLocationSignedDarkOak(random, dx, yOffset, dz, currentRadius, doubleTrunk))
                {
                    var pos = origin + new Vector(dx, yOffset, dz);
                    await FoliagePlacerHelper.TryPlaceLeaf(world, pos, foliageBlock, placedPositions);
                }
            }
        }
    }

    /// <summary>
    /// Dark oak's custom shouldSkipLocationSigned implementation.
    /// Adds extra logic for the middle layer (y=0) of 2x2 trunks.
    /// </summary>
    private bool ShouldSkipLocationSignedDarkOak(
        Random random,
        int dx,
        int y,
        int dz,
        int currentRadius,
        bool doubleTrunk)
    {
        // Special case for y=0 (middle layer) with double trunk
        // Skip if on the outer edge at specific corners
        if (y == 0 && doubleTrunk)
        {
            bool atNegativeEdgeX = dx == -currentRadius;
            bool atNegativeEdgeZ = dz == -currentRadius;
            bool beforePositiveEdgeX = dx < currentRadius;
            bool beforePositiveEdgeZ = dz < currentRadius;

            // Skip corner positions: must be at negative edge for both axes,
            // or before positive edge for both axes (exclusive corners)
            if ((atNegativeEdgeX && atNegativeEdgeZ) ||
                (!atNegativeEdgeX && beforePositiveEdgeX && !atNegativeEdgeZ && beforePositiveEdgeZ))
            {
                return true;
            }
        }

        // For all other cases, use standard skip logic
        return ShouldSkipLocationSigned(random, dx, y, dz, currentRadius, doubleTrunk);
    }

    /// <summary>
    /// Wrapper that handles signed coordinates for 2x2 trunks before calling ShouldSkipLocation.
    /// </summary>
    private bool ShouldSkipLocationSigned(
        Random random,
        int dx,
        int y,
        int dz,
        int currentRadius,
        bool doubleTrunk)
    {
        int minDx, minDz;

        if (doubleTrunk)
        {
            minDx = Math.Min(Math.Abs(dx), Math.Abs(dx - 1));
            minDz = Math.Min(Math.Abs(dz), Math.Abs(dz - 1));
        }
        else
        {
            minDx = Math.Abs(dx);
            minDz = Math.Abs(dz);
        }

        return ShouldSkipLocation(random, minDx, y, minDz, currentRadius, doubleTrunk);
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // Bottom layer (y=-1) logic for single trunk
        if (y == -1 && !doubleTrunk)
        {
            // Skip only the farthest corners
            return dx == currentRadius && dz == currentRadius;
        }

        // Top layer (y=1) logic
        if (y == 1)
        {
            // Skip positions where dx + dz is too large (creates rounded top)
            // This creates a diamond-like pattern that rounds off the top
            return dx + dz > currentRadius * 2 - 2;
        }

        // Don't skip other positions
        return false;
    }
}
