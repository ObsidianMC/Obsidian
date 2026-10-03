using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.IntProviders;

[ConfiguredFeatureProperty(IntProviderTypes.Constant)]
public sealed class ConstantIntProvider : IIntProvider
{
    public string Type { get; init; } = IntProviderTypes.Constant;

    public required int Value { get; init; }

    public int MinValue => this.Value;

    public int MaxValue => this.Value;

    public int Sample(IRandomSource random) => this.Value;
}
