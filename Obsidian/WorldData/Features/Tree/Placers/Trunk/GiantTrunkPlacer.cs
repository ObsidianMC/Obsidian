namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// A 2x2 trunk (mega spruce/pine); the top block only uses the origin column.
/// </summary>
[ConfiguredFeatureProperty("minecraft:giant_trunk_placer")]
public class GiantTrunkPlacer : TrunkPlacer
{
    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var below = origin + Vector.Down;
        SetDirtAt(tree, below);
        SetDirtAt(tree, below + Vector.East);
        SetDirtAt(tree, below + Vector.South);
        SetDirtAt(tree, below + Vector.South + Vector.East);

        for (var y = 0; y < freeTreeHeight; y++)
        {
            this.PlaceLogIfFree(tree, origin + (0, y, 0));
            if (y < freeTreeHeight - 1)
            {
                this.PlaceLogIfFree(tree, origin + (1, y, 0));
                this.PlaceLogIfFree(tree, origin + (1, y, 1));
                this.PlaceLogIfFree(tree, origin + (0, y, 1));
            }
        }

        return [new FoliageAttachment(origin + (0, freeTreeHeight, 0), 0, true)];
    }
}
