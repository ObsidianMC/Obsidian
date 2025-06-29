namespace Obsidian.Providers.IntProviders;

[TreeProperty(IntProviderTypes.Constant)]
public sealed class ConstantIntProvider : IIntProvider
{
    public required string Type { get; init; } = IntProviderTypes.Constant;

    public int Value { get; set; }

    //TODO
    public int Get() => 0;

}
