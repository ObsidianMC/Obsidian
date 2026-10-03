namespace Obsidian.WorldData.Features;

/// <summary>
/// Scatters sea pickles (1-4 per block) on the ocean floor around the origin, like vanilla's SeaPickleFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:sea_pickle")]
public sealed class SeaPickleFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:sea_pickle";

    /// <summary>
    /// Number of attempts.
    /// </summary>
    public required IIntProvider Count { get; init; }

    private static IBlock SeaPickle => field ??= BlocksRegistry.Get(Material.SeaPickle);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var placed = 0;
        var count = this.Count.Sample(random);
        for (var i = 0; i < count; i++)
        {
            var dx = random.NextInt(8) - random.NextInt(8);
            var dz = random.NextInt(8) - random.NextInt(8);
            var x = origin.X + dx;
            var z = origin.Z + dz;
            var position = new Vector(x, level.GetHeight(HeightmapType.OceanFloor, x, z), z);

            // The pickle count is drawn before the position is checked.
            var state = SeaPickle.WithProperty("pickles", random.NextInt(4) + 1);
            if (level.GetBlock(position).Material == Material.Water && state.CanSurvive(level, position))
            {
                level.SetBlock(position, state);
                placed++;
            }
        }

        return placed > 0;
    }
}
