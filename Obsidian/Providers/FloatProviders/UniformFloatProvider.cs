using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.FloatProviders;

/// <summary>
/// Uniform float in <c>[MinInclusive, MaxExclusive)</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:uniform")]
public sealed class UniformFloatProvider : IFloatProvider
{
    public string Type { get; init; } = "minecraft:uniform";

    public required float MinInclusive { get; init; }

    public required float MaxExclusive { get; init; }

    public float MinValue => this.MinInclusive;

    public float MaxValue => this.MaxExclusive;

    public float Sample(IRandomSource random) => random.NextFloat() * (this.MaxExclusive - this.MinInclusive) + this.MinInclusive;
}
