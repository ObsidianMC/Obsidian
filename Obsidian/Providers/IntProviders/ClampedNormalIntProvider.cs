namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.ClampedNormal)]
public sealed class ClampedNormalIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.ClampedNormal;

    public IntProviderRangeValue Value { get; init; }

    //TODO
    public int Get() => 0;
}
