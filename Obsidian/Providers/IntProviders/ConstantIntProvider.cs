namespace Obsidian.Providers.IntProviders;
public sealed class ConstantIntProvider : IIntProvider
{
    public IntProviderType ProviderType => IntProviderType.Constant;

    public int Value { get; set; }

}
