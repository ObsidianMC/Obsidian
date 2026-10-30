namespace Obsidian.SourceGenerators.Registry.Models;

/// <summary>A potion's protocol id, for the generated potion holder-set tags.</summary>
internal sealed class PotionEntry(string tag, int registryId) : ITaggable
{
    public string Tag { get; } = tag;

    public string Type => "potion";

    public string Parent => "potion";

    public string GetTagValue() => registryId.ToString();
}
