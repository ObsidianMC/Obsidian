namespace Obsidian.WorldData.Features;

/// <summary>
/// One seagrass (or, with <see cref="Probability"/>, tall seagrass) on the ocean floor near the origin, like vanilla's
/// SeagrassFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:seagrass")]
public sealed class SeagrassFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:seagrass";

    /// <summary>
    /// Chance of tall seagrass.
    /// </summary>
    public required float Probability { get; init; }

    private static IBlock Seagrass => field ??= BlocksRegistry.Get(Material.Seagrass);

    private static IBlock TallSeagrass => field ??= BlocksRegistry.Get(Material.TallSeagrass);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var dx = random.NextInt(8) - random.NextInt(8);
        var dz = random.NextInt(8) - random.NextInt(8);
        var x = origin.X + dx;
        var z = origin.Z + dz;
        var position = new Vector(x, level.GetHeight(HeightmapType.OceanFloor, x, z), z);
        if (level.GetBlock(position).Material != Material.Water)
            return false;

        var tall = random.NextDouble() < this.Probability;
        var state = tall ? TallSeagrass : Seagrass;
        if (!state.CanSurvive(level, position))
            return false;

        if (tall)
        {
            var above = position + Vector.Up;
            if (level.GetBlock(above).Material == Material.Water)
            {
                level.SetBlock(position, state);
                level.SetBlock(above, state.WithProperty("half", "upper"));
            }
        }
        else
        {
            level.SetBlock(position, state);
        }

        return true;
    }
}
