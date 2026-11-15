using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
/// <summary>
/// Represents a armor trim material registry entry and its synchronized element data.
/// </summary>
public sealed class TrimMaterialCodec : ICodec
{
    /// <summary>
    /// Gets the entry's namespaced registry identifier.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the numeric registry ID used on the network.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the armor trim material data serialized for this entry.
    /// </summary>
    public required TrimMaterialElement Element { get; init; }

    /// <summary>
    /// Writes this entry's armor trim material fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer)
    {
        var description = new NbtCompound("description")
        {
            new NbtTag<string>("translate", this.Element.Description.Translate)
        };
        if (this.Element.Description.Color is string color)
            description.Add(new NbtTag<string>("color", color));

        writer.WriteString("palette_id", this.Element.PaletteId);
        writer.WriteTag(description);
    }
}
