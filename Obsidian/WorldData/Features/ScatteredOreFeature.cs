using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Scattered single ore blocks around the origin (ancient debris), like vanilla's ScatteredOreFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:scattered_ore")]
public sealed class ScatteredOreFeature : ConfiguredFeatureBase
{
    private const int MaxDistanceFromOrigin = 7;

    public override string Type => "minecraft:scattered_ore";

    public required ImmutableArray<OreTarget> Targets { get; init; }

    /// <summary>
    /// Up to this many blocks are attempted (the count is <c>nextInt(Size + 1)</c>).
    /// </summary>
    public required int Size { get; init; }

    /// <summary>
    /// Chance to skip a position that touches air (0 never skips, 1 never places next to air).
    /// </summary>
    public float DiscardChanceOnAirExposure { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var count = random.NextInt(this.Size + 1);
        for (var i = 0; i < count; i++)
        {
            var spread = Math.Min(i, MaxDistanceFromOrigin);
            var x = RandomOffset(random, spread);
            var y = RandomOffset(random, spread);
            var z = RandomOffset(random, spread);
            var position = origin + new Vector(x, y, z);
            var existing = level.GetBlock(position);

            foreach (var target in this.Targets)
            {
                if (this.CanPlaceOre(level, existing, random, target, position))
                {
                    level.SetBlock(position, target.Block);
                    break;
                }
            }
        }

        return true;
    }

    // Java Math.round((nextFloat() - nextFloat()) * spread).
    private static int RandomOffset(IRandomSource random, int spread) => FeatureHelpers.JavaRound((random.NextFloat() - random.NextFloat()) * spread);

    // OreFeature.canPlaceOre: the rule must match, then the air-exposure roll decides whether touching air matters.
    private bool CanPlaceOre(IWorldGenLevel level, IBlock existing, IRandomSource random, OreTarget target, Vector position)
    {
        if (!target.Target.Test(existing, random))
            return false;

        return this.ShouldSkipAirCheck(random) || !FeatureHelpers.IsAdjacentToAir(level, position);
    }

    private bool ShouldSkipAirCheck(IRandomSource random)
    {
        if (this.DiscardChanceOnAirExposure <= 0.0f)
            return true;

        return this.DiscardChanceOnAirExposure < 1.0f && random.NextFloat() >= this.DiscardChanceOnAirExposure;
    }
}
