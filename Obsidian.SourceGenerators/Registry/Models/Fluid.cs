namespace Obsidian.SourceGenerators.Registry.Models;
public sealed class Fluid(string tag, string name, int registryId) : ITaggable, IHasName, IRegistryItem
{
    public string Tag { get; } = tag;

    public string Name { get; } = name;

    public int RegistryId { get; } = registryId;

    public string Type => "fluid";

    public string Parent => "fluid";

    public string GetTagValue() => this.RegistryId.ToString();
}
