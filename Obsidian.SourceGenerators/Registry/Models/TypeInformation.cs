namespace Obsidian.SourceGenerators.Registry.Models;
public readonly struct TypeInformation(INamedTypeSymbol symbol, string resourceLocation)
{
    public INamedTypeSymbol Symbol { get; } = symbol;

    public string ResourceLocation { get; } = resourceLocation;

    public List<ISymbol> GetProperties()
    {
        var members = this.Symbol.GetMembers().Where(x => x.Kind == SymbolKind.Property).ToList();

        if (this.Symbol.BaseType != null)
            members.AddRange(this.Symbol.BaseType.GetMembers().Where(x => x.Kind == SymbolKind.Property));

        foreach (var mem in members.ToList())
        {
            var memProp = (IPropertySymbol)mem;
            if (memProp.Type.Kind == SymbolKind.NamedType)
                members.AddRange(memProp.Type.GetMembers().Where(x => x.Kind == SymbolKind.Property));
        }

        return members;
    }
}
