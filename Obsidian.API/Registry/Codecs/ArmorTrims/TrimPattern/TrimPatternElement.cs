namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
public sealed class TrimPatternElement
{
    public required string TemplateItem { get; init; }

    public required TrimDescription Description { get; init; }

    public required string AssetId { get; init; }

    public required bool Decal { get; init; }
}
