namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.Clamped)]
public sealed class ClampedIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.Clamped;

    public IntProviderRangeValue Value { get; init; }

    public IIntProvider Source { get; init; } = default!;

    //TODO
    public int Get() => 0;
}
