using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
public sealed class TrimPatternCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required TrimPatternElement Element { get; init; }

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
