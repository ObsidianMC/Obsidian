namespace Obsidian.SourceGenerators.Registry.Models;

internal interface ITaggable
{
    public string Tag { get; }
    public string GetTagValue();
}
