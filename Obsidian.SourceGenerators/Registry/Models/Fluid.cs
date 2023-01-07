namespace Obsidian.SourceGenerators.Registry.Models;
public sealed class Fluid : ITaggable, IHasName, IRegistryItem
{
    public string Tag { get; }

    public string Name { get; }

    public int RegistryId { get; }

    public Fluid(string tag, string name, int registryId)
    {
        this.Tag = tag;
        this.Name = name;
        this.RegistryId = registryId;
    }

    public string GetTagValue() => this.RegistryId.ToString();
}
