namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
/// <summary>
/// Contains an armor trim material's palette identifier and translated description.
/// </summary>
public sealed record class TrimMaterialElement : INetworkSerializable<TrimMaterialElement>
{
    /// <summary>The palette the trim is colored with, like <c>minecraft:trim/iron</c>.</summary>
    public required string PaletteId { get; init; }

    /// <summary>
    /// Gets the material's translated display description.
    /// </summary>
    public required TrimDescription Description { get; init; }

    /// <summary>
    /// Reads a palette identifier followed by its translated description from the network stream.
    /// </summary>
    public static TrimMaterialElement Read(INetStreamReader reader) => new()
    {
        PaletteId = reader.ReadString(),
        Description = TrimDescription.Read(reader)
    };

    /// <summary>
    /// Writes a palette identifier followed by its translated description to the network stream.
    /// </summary>
    public static void Write(TrimMaterialElement element, INetStreamWriter writer)
    {
        writer.WriteString(element.PaletteId);
        TrimDescription.Write(element.Description, writer);
    }
}
