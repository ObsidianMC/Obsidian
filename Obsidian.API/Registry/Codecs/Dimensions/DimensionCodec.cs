using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dimensions;

public sealed record class DimensionCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    /// <summary>
    /// The entry's data. A plugin's entry gets a new element when its plugin reloads, and keeps its instance and id.
    /// </summary>
    public required DimensionElement Element { get; set; }

    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}
