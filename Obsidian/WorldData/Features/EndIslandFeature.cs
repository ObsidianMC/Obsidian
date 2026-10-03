using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A small floating end stone island that narrows downward, like vanilla's EndIslandFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:end_island")]
public sealed class EndIslandFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:end_island";

    private static IBlock EndStone => field ??= BlocksRegistry.Get(Material.EndStone);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var radius = random.NextInt(3) + 4.0f;
        for (var y = 0; radius > 0.5f; y--)
        {
            for (var x = Mth.Floor(-radius); x <= FeatureHelpers.Ceil(radius); x++)
            {
                for (var z = Mth.Floor(-radius); z <= FeatureHelpers.Ceil(radius); z++)
                {
                    if (x * x + z * z <= (radius + 1.0f) * (radius + 1.0f))
                        level.SetBlock(origin + new Vector(x, y, z), EndStone);
                }
            }

            radius -= random.NextInt(2) + 0.5f;
        }

        return true;
    }
}
