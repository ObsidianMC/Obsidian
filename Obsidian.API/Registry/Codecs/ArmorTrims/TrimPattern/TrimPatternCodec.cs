using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
/// <summary>
/// Represents a armor trim pattern registry entry and its synchronized element data.
/// </summary>
public sealed class TrimPatternCodec : ICodec
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
    /// Gets the armor trim pattern data serialized for this entry.
    /// </summary>
    public required TrimPatternElement Element { get; init; }

    /// <summary>
    /// Writes this entry's armor trim pattern fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer)
    {
        var description = new NbtList(NbtTagType.String, "description")
        {
            new NbtTag<string>("translate", this.Element.Description.Translate)
        };

        writer.WriteString("asset_id", this.Element.AssetId);
        writer.WriteBool("decal", this.Element.Decal);
        writer.WriteTag(description);
    }
}
