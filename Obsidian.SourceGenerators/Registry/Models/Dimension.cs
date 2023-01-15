using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Dimension : IHasName, IRegistryItem
{
    public string Name { get; }

    public int RegistryId { get; }

    public Dictionary<string, JsonElement> Properties { get; set; } = new();

    public Dimension(string name, int registryId)
    {
        this.Name = name;
        this.RegistryId = registryId;
    }
}
