namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.Uniform)]
[TreeProperty(IntProviderTypes.BiasedToBottom)]
public sealed class RangedIntProvider : IIntProvider
{
    public required string Type { get; init; }

    public IntProviderRangeValue Value { get; init; }

    //TODO
    public int Get() => 0;
}
