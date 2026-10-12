namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class EntityEntry(string tag, int registryId) : ITaggable
{
    public string Tag { get; } = tag;
    public string Type => "entity_type";
    public string Parent => "entity_type";
    public string GetTagValue() => registryId.ToString();
}
