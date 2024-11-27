namespace Obsidian.SourceGenerators.Registry.Models;
internal readonly struct Sound(string name, int registryId) : IRegistryItem
{
    public string Name { get; } = name; 
    public int RegistryId { get; } = registryId;
}
