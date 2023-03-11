namespace Obsidian.SourceGenerators.Registry.Models;
public readonly struct EntityClass
{
    public INamedTypeSymbol Symbol { get; }

    public string EntityResourceLocation { get; }

    public EntityClass(INamedTypeSymbol symbol, string resourceLocation)
    {
        this.Symbol = symbol;
        this.EntityResourceLocation = resourceLocation;
    }
}
