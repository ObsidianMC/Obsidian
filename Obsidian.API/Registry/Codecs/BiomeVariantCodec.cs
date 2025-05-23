
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs;
public sealed class BiomeVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required BiomeVariantElement Element { get; set; }

    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}
