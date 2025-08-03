namespace Obsidian.SourceGenerators.Registry.Models;

internal interface ITaggable
{
    public string Type { get; }
    public string Tag { get; }
    public string Parent { get; }
    public string GetTagValue();
}
