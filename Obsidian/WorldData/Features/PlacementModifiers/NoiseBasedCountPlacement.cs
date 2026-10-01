using Obsidian.WorldData.Generators.Mojang;

using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Repeats the position a number of times proportional to the biome info noise.
/// </summary>
[ConfiguredFeatureProperty("minecraft:noise_based_count")]
public sealed class NoiseBasedCountPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:noise_based_count";

    public required int NoiseToCountRatio { get; init; }

    public required double NoiseFactor { get; init; }

    public double NoiseOffset { get; init; }

    public override IEnumerable<Vector> GetPositions(PlacementContext context, IRandomSource random, Vector position)
    {
        var noise = BiomeTemperature.BiomeInfoNoise.GetValue(position.X / this.NoiseFactor, position.Z / this.NoiseFactor, false);
        var count = (int)Math.Ceiling((noise + this.NoiseOffset) * this.NoiseToCountRatio);
        return Enumerable.Repeat(position, Math.Max(count, 0));
    }
}
