using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

/// <summary>
/// A <see cref="GiantTrunkPlacer"/> trunk with short diagonal branches in its upper half, each topped by foliage.
/// </summary>
[ConfiguredFeatureProperty("minecraft:mega_jungle_trunk_placer")]
public sealed class MegaJungleTrunkPlacer : GiantTrunkPlacer
{
    public override List<FoliageAttachment> PlaceTrunk(TreeContext tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var attachments = new List<FoliageAttachment>();
        attachments.AddRange(base.PlaceTrunk(tree, freeTreeHeight, origin));

        for (var branchY = freeTreeHeight - 2 - random.NextInt(4); branchY > freeTreeHeight / 2; branchY -= 2 + random.NextInt(4))
        {
            var angle = random.NextFloat() * (float)(Math.PI * 2);
            var x = 0;
            var z = 0;
            for (var i = 0; i < 5; i++)
            {
                // float math, truncated toward zero like Java's (int) cast.
                x = (int)(1.5f + Mth.Cos(angle) * i);
                z = (int)(1.5f + Mth.Sin(angle) * i);
                this.PlaceLog(tree, origin + (x, branchY - 3 + i / 2, z));
            }

            attachments.Add(new FoliageAttachment(origin + (x, branchY, z), -2, false));
        }

        return attachments;
    }
}
