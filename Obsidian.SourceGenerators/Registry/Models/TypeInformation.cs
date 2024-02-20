namespace Obsidian.SourceGenerators.Registry.Models;
public readonly struct TypeInformation(INamedTypeSymbol symbol, string resourceLocation)
{
    public INamedTypeSymbol Symbol { get; } = symbol;

    public string ResourceLocation { get; } = resourceLocation;
}
