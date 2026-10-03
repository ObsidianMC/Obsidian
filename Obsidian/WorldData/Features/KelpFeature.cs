namespace Obsidian.WorldData.Features;

/// <summary>
/// A kelp column growing up from the ocean floor, like vanilla's KelpFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:kelp")]
public sealed class KelpFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:kelp";

    private static IBlock Kelp => field ??= BlocksRegistry.Get(Material.Kelp);

    private static IBlock KelpPlant => field ??= BlocksRegistry.Get(Material.KelpPlant);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var placed = 0;
        var position = origin.AtY(level.GetHeight(HeightmapType.OceanFloor, origin.X, origin.Z));
        if (level.GetBlock(position).Material != Material.Water)
            return false;

        var height = 1 + random.NextInt(10);
        for (var i = 0; i <= height; i++)
        {
            if (level.GetBlock(position).Material == Material.Water
                && level.GetBlock(position + Vector.Up).Material == Material.Water
                && KelpPlant.CanSurvive(level, position))
            {
                if (i == height)
                {
                    level.SetBlock(position, Kelp.WithProperty("age", random.NextInt(4) + 20));
                    placed++;
                }
                else
                {
                    level.SetBlock(position, KelpPlant);
                }
            }
            else if (i > 0)
            {
                // Cap the column with a kelp head one block lower. A failed first block just moves up and retries.
                var below = position + Vector.Down;
                if (Kelp.CanSurvive(level, below) && level.GetBlock(below + Vector.Down).Material != Material.Kelp)
                {
                    level.SetBlock(below, Kelp.WithProperty("age", random.NextInt(4) + 20));
                    placed++;
                }

                break;
            }

            position += Vector.Up;
        }

        return placed > 0;
    }
}
