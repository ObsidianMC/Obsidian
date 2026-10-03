namespace Obsidian.SourceGenerators.Registry.Models;

/// <summary>
/// A biome from the biome codecs, so biome tags (<c>worldgen/biome</c>) resolve to biome codec ids.
/// </summary>
internal sealed class BiomeEntry(string tag, int registryId) : ITaggable
{
    public string Tag { get; } = tag;

    public string Type => "worldgen/biome";

    public string Parent => "worldgen";

    public string GetTagValue() => registryId.ToString();
}
