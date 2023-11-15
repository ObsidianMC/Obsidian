namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
public sealed class TrimMaterialCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required TrimMaterialElement Element { get; init; }
}
