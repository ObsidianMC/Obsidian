namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// Dark oak trunk: a 2x2 trunk that may lean near the top, plus short random log stubs around the top.
/// </summary>
[ConfiguredFeatureProperty("minecraft:dark_oak_trunk_placer")]
public sealed class DarkOakTrunkPlacer : TrunkPlacer
{
    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var attachments = new List<FoliageAttachment>();
        var below = origin + Vector.Down;
        SetDirtAt(tree, below);
        SetDirtAt(tree, below + Vector.East);
        SetDirtAt(tree, below + Vector.South);
        SetDirtAt(tree, below + Vector.South + Vector.East);

        var leanDirection = TreeDirections.RandomHorizontal(random);
        var leanHeight = freeTreeHeight - random.NextInt(4);
        var leanSteps = 2 - random.NextInt(3);
        var trunkX = origin.X;
        var trunkZ = origin.Z;
        var topY = origin.Y + freeTreeHeight - 1;

        for (var i = 0; i < freeTreeHeight; i++)
        {
            if (i >= leanHeight && leanSteps > 0)
            {
                var step = leanDirection.ToVector();
                trunkX += step.X;
                trunkZ += step.Z;
                leanSteps--;
            }

            var position = new Vector(trunkX, origin.Y + i, trunkZ);
            if (TreeBlocks.IsAirOrLeaves(tree.Level, position))
            {
                this.PlaceLog(tree, position);
                this.PlaceLog(tree, position + Vector.East);
                this.PlaceLog(tree, position + Vector.South);
                this.PlaceLog(tree, position + Vector.East + Vector.South);
            }
        }

        attachments.Add(new FoliageAttachment(new Vector(trunkX, topY, trunkZ), 0, true));

        for (var dx = -1; dx <= 2; dx++)
        {
            for (var dz = -1; dz <= 2; dz++)
            {
                // Only the ring around the 2x2 trunk, each spot with a 1/3 chance.
                if ((dx < 0 || dx > 1 || dz < 0 || dz > 1) && random.NextInt(3) <= 0)
                {
                    var length = random.NextInt(3) + 2;
                    for (var i = 0; i < length; i++)
                        this.PlaceLog(tree, new Vector(origin.X + dx, topY - i - 1, origin.Z + dz));

                    attachments.Add(new FoliageAttachment(new Vector(origin.X + dx, topY, origin.Z + dz), 0, false));
                }
            }
        }

        return attachments;
    }
}
