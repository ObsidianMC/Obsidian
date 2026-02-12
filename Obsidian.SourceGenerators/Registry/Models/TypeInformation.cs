namespace Obsidian.SourceGenerators.Registry.Models;
public record struct TypeInformation
{
    public INamedTypeSymbol Symbol { get; }

    public string ResourceLocation { get; }
    public bool IsConfiguredFeature { get; internal set; }

    public TypeInformation(INamedTypeSymbol symbol, string resourceLocation, bool isConfiguredFeature)
    {
        this.Symbol = symbol;
        this.ResourceLocation = resourceLocation;
        this.IsConfiguredFeature = isConfiguredFeature;
    }

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
