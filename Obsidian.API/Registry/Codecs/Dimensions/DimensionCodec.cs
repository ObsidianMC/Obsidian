using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.Dimensions;

[JsonConverter(typeof(DimensionCodecConverter))]
public sealed record class DimensionCodec : ICodec
{
    public string Name { get; set; }

    public int Id { get; internal set; }

    public DimensionElement Element { get; internal set; } = new();

    internal DimensionCodec() { }
}
