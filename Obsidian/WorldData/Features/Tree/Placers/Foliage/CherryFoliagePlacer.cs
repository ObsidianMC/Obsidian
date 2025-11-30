using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for cherry trees - creates a rounded canopy with holes and hanging leaves.
/// </summary>
[TreeProperty("minecraft:cherry_foliage_placer")]
public sealed class CherryFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// Height of the cherry foliage (4-16 blocks).
    /// </summary>
    [Range(4, 16)]
    public required IIntProvider Height { get; set; }

    /// <summary>
    /// Chance for holes on the wide bottom layer edges (0.0-1.0).
    /// </summary>
    [Range(0.0, 1.0)]
    public required float WideBottomLayerHoleChance { get; set; }

    /// <summary>
    /// Chance for holes at corners (0.0-1.0).
    /// </summary>
    [Range(0.0, 1.0)]
    public required float CornerHoleChance { get; set; }

    /// <summary>
    /// Chance for hanging leaves below the canopy (0.0-1.0).
    /// </summary>
    [Range(0.0, 1.0)]
    public required float HangingLeavesChance { get; set; }

    /// <summary>
    /// Chance for hanging leaves to extend an additional block down (0.0-1.0).
    /// </summary>
    [Range(0.0, 1.0)]
    public required float HangingLeavesExtensionChance { get; set; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        return Height.Get();
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
            bool doubleTrunk = false; // Cherry trees typically have single trunks
            int radiusOffset = 0; // Can be from attachment if available

            var foliagePos = attachment + new Vector(0, offset, 0);
            int currentRadius = leafRadius + radiusOffset - 1;

            // Top layers - smaller radius
            // Layer at foliageHeight - 3: radius - 2
            await FoliagePlacerHelper.PlaceLeavesRow(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                currentRadius - 2,
                foliageHeight - 3,
                doubleTrunk,
                (r, dx, y, dz, rad, dt) => ShouldSkipLocation(r, dx, y, dz, rad, dt),
                placedPositions
            );

            // Layer at foliageHeight - 4: radius - 1
            await FoliagePlacerHelper.PlaceLeavesRow(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                currentRadius - 1,
                foliageHeight - 4,
                doubleTrunk,
                (r, dx, y, dz, rad, dt) => ShouldSkipLocation(r, dx, y, dz, rad, dt),
                placedPositions
            );

            // Main body - full radius from foliageHeight - 5 down to 0
            for (int y = foliageHeight - 5; y >= 0; y--)
            {
                await FoliagePlacerHelper.PlaceLeavesRow(
                    context.World,
                    random,
                    foliageBlock,
                    foliagePos,
                    currentRadius,
                    y,
                    doubleTrunk,
                    (r, dx, yy, dz, rad, dt) => ShouldSkipLocation(r, dx, yy, dz, rad, dt),
                    placedPositions
                );
            }

            // Bottom layers with hanging leaves
            // Layer at y=-1: full radius with hanging leaves
            await FoliagePlacerHelper.PlaceLeavesRowWithHangingLeaves(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                currentRadius,
                -1,
                doubleTrunk,
                HangingLeavesChance,
                HangingLeavesExtensionChance,
                (r, dx, y, dz, rad, dt) => ShouldSkipLocation(r, dx, y, dz, rad, dt),
                placedPositions
            );

            // Layer at y=-2: radius - 1 with hanging leaves
            await FoliagePlacerHelper.PlaceLeavesRowWithHangingLeaves(
                context.World,
                random,
                foliageBlock,
                foliagePos,
                currentRadius - 1,
                -2,
                doubleTrunk,
                HangingLeavesChance,
                HangingLeavesExtensionChance,
                (r, dx, y, dz, rad, dt) => ShouldSkipLocation(r, dx, y, dz, rad, dt),
                placedPositions
            );
        }

        return placedPositions;
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // Special logic for the wide bottom layer (y == -1)
        if (y == -1)
        {
            // Skip edge positions with probability on the bottom layer
            if ((dx == currentRadius || dz == currentRadius) && random.NextSingle() < WideBottomLayerHoleChance)
            {
                return true;
            }
        }

        // Corner logic for all layers
        bool isCorner = dx == currentRadius && dz == currentRadius;
        bool isWideLayer = currentRadius > 2;

        if (isWideLayer)
        {
            // For wide layers: skip corners OR positions where dx + dz is large
            return isCorner || (dx + dz > currentRadius * 2 - 2 && random.NextSingle() < CornerHoleChance);
        }
        else
        {
            // For narrow layers: only skip corners with probability
            return isCorner && random.NextSingle() < CornerHoleChance;
        }
    }
}
