namespace Obsidian.API.Registry.Codecs.Dimensions;

public sealed class DimensionCodec : ICodec
{
    public string Name { get; set; }

    public int Id { get; set; }

    public DimensionElement Element { get; set; }
}
