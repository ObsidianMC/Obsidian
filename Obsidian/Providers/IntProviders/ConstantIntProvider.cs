namespace Obsidian.Providers.IntProviders;

[ConfiguredFeatureProperty(IntProviderTypes.Constant)]
public sealed class ConstantIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.Constant;

    public int Value { get; set; }

    public int Get() => this.Value;

}
