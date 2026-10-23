namespace Obsidian.SourceGenerators.Registry.Models;

/// <summary>
/// An entity type from vanilla's entity data, so entity type tags (<c>entity_type</c>) resolve to entity type ids.
/// </summary>
internal sealed class EntityTypeEntry(string tag, int registryId) : ITaggable
{
    public string Tag { get; } = tag;

    public string Type => "entity_type";

    public string Parent => "entity_type";

    public string GetTagValue() => registryId.ToString();
}
