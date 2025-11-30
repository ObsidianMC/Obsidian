using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for azalea trees - randomly places leaves in a scattered pattern.
/// Creates a natural, irregular canopy by placing individual leaf blocks randomly.
/// </summary>
[TreeProperty("minecraft:random_spread_foliage_placer")]
public sealed class RandomSpreadFoliagePlacer : FoliagePlacer
{
    public override required string Type { get; init; }

    /// <summary>
    /// Height range for random leaf placement (1-512 blocks).
    /// </summary>
    [Range(1, 512)]
    public required IIntProvider FoliageHeight { get; set; }

    /// <summary>
    /// Number of attempts to place individual leaf blocks (0-256).
    /// Higher values create denser foliage.
    /// </summary>
    [Range(0, 256)]
    public required int LeafPlacementAttempts { get; set; }

    public override int GetFoliageHeight(Random random, int treeHeight)
    {
        return FoliageHeight.Get();
    }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock)
    {
        var random = context.Random;
        var placedPositions = new List<Vector>();

        foreach (var origin in trunkPositions)
        {
            int leafRadius = FoliageRadius(random, treeHeight);
            int foliageHeight = GetFoliageHeight(random, treeHeight);

            // Place leaves randomly within a box around the origin
            for (int i = 0; i < LeafPlacementAttempts; i++)
            {
                // Calculate random offset in each direction
                // Uses: random.nextInt(leafRadius) - random.nextInt(leafRadius)
                // This creates a range of [-leafRadius+1, leafRadius-1] with bias toward center
                int xOffset = random.Next(leafRadius) - random.Next(leafRadius);
                int yOffset = random.Next(foliageHeight) - random.Next(foliageHeight);
                int zOffset = random.Next(leafRadius) - random.Next(leafRadius);

                var pos = origin + new Vector(xOffset, yOffset, zOffset);

                // Try to place a leaf at this random position
                await FoliagePlacerHelper.TryPlaceLeaf(context.World, pos, foliageBlock, placedPositions);
            }
        }

        return placedPositions;
    }

    protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
    {
        // Random spread doesn't use structured layers, so never skip in the helper
        // (This method is not used by this placer since we place leaves individually)
        return false;
    }
}
