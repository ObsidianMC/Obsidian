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
