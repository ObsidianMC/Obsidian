namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
public sealed class TrimPatternCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required TrimPatternElement Element { get; init; }
}
