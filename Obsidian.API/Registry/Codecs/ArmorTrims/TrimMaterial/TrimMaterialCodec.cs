using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
public sealed class TrimMaterialCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required TrimMaterialElement Element { get; init; }

    public void WriteElement(INbtWriter writer)
    {
        var description = new NbtList(NbtTagType.String, "description")
        {
            new NbtTag<string>("translate", this.Element.Description.Translate),
            new NbtTag<string>("color", this.Element.Description.Color!)
        };

        writer.WriteString("palette_id", this.Element.PaletteId);
        writer.WriteTag(description);
    }
}
