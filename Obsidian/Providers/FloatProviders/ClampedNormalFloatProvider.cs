using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.FloatProviders;

/// <summary>
/// Normally distributed float (<c>Mean</c>, <c>Deviation</c>) clamped to <c>[Min, Max]</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:clamped_normal")]
public sealed class ClampedNormalFloatProvider : IFloatProvider
{
    public string Type { get; init; } = "minecraft:clamped_normal";

    public required float Mean { get; init; }

    public required float Deviation { get; init; }

    public required float Min { get; init; }

    public required float Max { get; init; }

    public float MinValue => this.Min;

    public float MaxValue => this.Max;

    public float Sample(IRandomSource random) => Math.Clamp(this.Mean + (float)random.NextGaussian() * this.Deviation, this.Min, this.Max);
}
