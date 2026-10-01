namespace Obsidian.WorldData.Features;

/// <summary>
/// Tries a placed feature at random offsets around the origin, like vanilla's RandomPatchFeature (grass, flowers, berries...).
/// </summary>
[ConfiguredFeatureClass("minecraft:random_patch")]
public sealed class RandomPatchFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:random_patch";

    public int Tries { get; init; } = 128;

    public int XzSpread { get; init; } = 7;

    public int YSpread { get; init; } = 3;

    public required PlacedFeature Feature { get; init; }

    public override bool Place(FeatureContext context) =>
        context.Level.EnsureCanWrite(context.Origin) && PlacePatch(context, this.Tries, this.XzSpread, this.YSpread, this.Feature);

    /// <summary>
    /// Shared by <c>random_patch</c>, <c>flower</c> and <c>no_bonemeal_flower</c>, which are the same vanilla class.
    /// Each try draws x, y, z offsets as <c>nextInt(spread + 1) - nextInt(spread + 1)</c>, in that order.
    /// </summary>
    internal static bool PlacePatch(FeatureContext context, int tries, int xzSpread, int ySpread, PlacedFeature feature)
    {
        var random = context.Random;
        var origin = context.Origin;
        var xzBound = xzSpread + 1;
        var yBound = ySpread + 1;
        var placed = 0;

        for (var i = 0; i < tries; i++)
        {
            var x = random.NextInt(xzBound) - random.NextInt(xzBound);
            var y = random.NextInt(yBound) - random.NextInt(yBound);
            var z = random.NextInt(xzBound) - random.NextInt(xzBound);

            if (feature.Place(context.Level, context.Generation, random, origin + new Vector(x, y, z)))
                placed++;
        }

        return placed > 0;
    }
}
