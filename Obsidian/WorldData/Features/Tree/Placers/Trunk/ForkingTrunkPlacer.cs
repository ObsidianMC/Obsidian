namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// Acacia trunk: leans one way near the top and may fork a second branch in another direction.
/// </summary>
[ConfiguredFeatureProperty("minecraft:forking_trunk_placer")]
public sealed class ForkingTrunkPlacer : TrunkPlacer
{
    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        SetDirtAt(tree, origin + Vector.Down);

        var attachments = new List<FoliageAttachment>();
        var leanDirection = TreeDirections.RandomHorizontal(random);
        var leanHeight = freeTreeHeight - random.NextInt(4) - 1;
        var leanSteps = 3 - random.NextInt(3);
        var x = origin.X;
        var z = origin.Z;
        int? topY = null;

        for (var i = 0; i < freeTreeHeight; i++)
        {
            var y = origin.Y + i;
            if (i >= leanHeight && leanSteps > 0)
            {
                var step = leanDirection.ToVector();
                x += step.X;
                z += step.Z;
                leanSteps--;
            }

            if (this.PlaceLog(tree, new Vector(x, y, z)))
                topY = y + 1;
        }

        if (topY is not null)
            attachments.Add(new FoliageAttachment(new Vector(x, topY.Value, z), 1, false));

        x = origin.X;
        z = origin.Z;
        var branchDirection = TreeDirections.RandomHorizontal(random);
        if (branchDirection != leanDirection)
        {
            var branchStart = leanHeight - random.NextInt(2) - 1;
            var branchLength = 1 + random.NextInt(3);
            topY = null;

            for (var i = branchStart; i < freeTreeHeight && branchLength > 0; branchLength--)
            {
                if (i >= 1)
                {
                    var y = origin.Y + i;
                    var step = branchDirection.ToVector();
                    x += step.X;
                    z += step.Z;
                    if (this.PlaceLog(tree, new Vector(x, y, z)))
                        topY = y + 1;
                }

                i++;
            }

            if (topY is not null)
                attachments.Add(new FoliageAttachment(new Vector(x, topY.Value, z), 0, false));
        }

        return attachments;
    }
}
