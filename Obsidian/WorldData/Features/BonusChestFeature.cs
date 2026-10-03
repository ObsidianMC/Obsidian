namespace Obsidian.WorldData.Features;

/// <summary>
/// The optional spawn bonus chest (with the <c>spawn_bonus_chest</c> loot table) with torches around it, like vanilla's
/// BonusChestFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:bonus_chest")]
public sealed class BonusChestFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:bonus_chest";

    private static IBlock Chest => field ??= BlocksRegistry.Get(Material.Chest);

    private static IBlock Torch => field ??= BlocksRegistry.Get(Material.Torch);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var minX = origin.X >> 4 << 4;
        var minZ = origin.Z >> 4 << 4;
        var xs = FeatureHelpers.ShuffledCopy(Enumerable.Range(minX, 16), random);
        var zs = FeatureHelpers.ShuffledCopy(Enumerable.Range(minZ, 16), random);

        foreach (var x in xs)
        {
            foreach (var z in zs)
            {
                var position = new Vector(x, level.GetHeight(HeightmapType.MotionBlockingNoLeaves, x, z), z);
                var existing = level.GetBlock(position);

                if (!existing.HasEmptyCollision())
                    continue;

                level.SetBlock(position, Chest);
                FeatureHelpers.SetLootTable(level, random, position, "minecraft:chests/spawn_bonus_chest");

                foreach (var face in FeatureHelpers.Horizontal)
                {
                    var side = position.Offset(face);
                    if (Torch.CanSurvive(level, side))
                        level.SetBlock(side, Torch);
                }

                return true;
            }
        }

        return false;
    }
}
