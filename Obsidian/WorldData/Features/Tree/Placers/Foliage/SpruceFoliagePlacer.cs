using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for spruce trees - creates a conical shape with gradually increasing radius.
/// </summary>
[TreeProperty("minecraft:spruce_foliage_placer")]
public sealed class SpruceFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// The height of the trunk before foliage starts. Used to calculate foliage height.
    /// </summary>
    public required IIntProvider TrunkHeight { get; init; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        return TrunkHeight.Get();
    }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock)
    {
        var random = context.Random;
        var placedPositions = new List<Vector>();

        foreach (var attachment in trunkPositions)
        {
            int leafRadius = FoliageRadius(random, treeHeight);
            int offset = GetOffset(random);
            int trunkHeight = TrunkHeight.Get();
            int foliageHeight = GetFoliageHeight(random, treeHeight);
            bool doubleTrunk = false; // Spruces are typically single trunk

            var foliagePos = attachment;

            // Initialize radius progression
            int currentRadius = random.Next(2); // Start with 0 or 1
            int maxRadius = 1;
            int minRadius = 0;
            int radiusOffset = 0; // Can be from foliage attachment if available

            // Place layers from top (offset) down to bottom (-foliageHeight)
            // This creates the conical spruce shape
            for (int yo = offset; yo >= -foliageHeight; yo--)
            {
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

                // Progress the radius for the next layer down
                if (currentRadius >= maxRadius)
                {
                    // Reset to minimum and increase the range
                    currentRadius = minRadius;
                    minRadius = 1;
                    maxRadius = Math.Min(maxRadius + 1, leafRadius + radiusOffset);
                }
                else
                {
                    currentRadius++;
                }
            }
        }

        return placedPositions;
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // Skip only the outermost corners to create a diamond/square-ish cross-section
        // This gives spruce trees their characteristic shape
        return dx == currentRadius && dz == currentRadius && currentRadius > 0;
    }
}
