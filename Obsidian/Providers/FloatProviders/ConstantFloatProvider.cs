using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.FloatProviders;

[ConfiguredFeatureProperty("minecraft:constant")]
public sealed class ConstantFloatProvider : IFloatProvider
{
    public string Type { get; init; } = "minecraft:constant";

    public required float Value { get; init; }

    public float MinValue => this.Value;

    public float MaxValue => this.Value;

    public float Sample(IRandomSource random) => this.Value;
}
