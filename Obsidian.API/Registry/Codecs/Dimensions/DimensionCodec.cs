namespace Obsidian.API.Registry.Codecs.Dimensions;

public sealed record class DimensionCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required DimensionElement Element { get; init; }
}
