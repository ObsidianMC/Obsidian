namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
public sealed record class TrimMaterialElement : INetworkSerializable<TrimMaterialElement>
{
    /// <summary>The palette the trim is colored with, like <c>minecraft:trim/iron</c>.</summary>
    public required string PaletteId { get; init; }

    public required TrimDescription Description { get; init; }

    public static TrimMaterialElement Read(INetStreamReader reader) => new()
    {
        PaletteId = reader.ReadString(),
        Description = TrimDescription.Read(reader)
    };

    public static void Write(TrimMaterialElement element, INetStreamWriter writer)
    {
        writer.WriteString(element.PaletteId);
        TrimDescription.Write(element.Description, writer);
    }
}
