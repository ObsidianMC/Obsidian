namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
public sealed class TrimMaterialElement
{
    public required string Ingredient { get; init; }

    public required string AssetName { get; init; }

    public required double ItemModelIndex { get; init; }

    public required TrimDescription Description { get; init; }

    public Dictionary<string, string>? OverrideArmorMaterials { get; init; }
}
