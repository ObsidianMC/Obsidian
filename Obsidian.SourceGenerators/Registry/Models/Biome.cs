using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Biome : IHasName, IRegistryItem
{
    public string Name { get; }

    public int RegistryId { get; }

    public Dictionary<string, JsonElement> Properties { get; set; } = new();

    public Biome(string name, int registryId)
    {
        this.Name = name;
        this.RegistryId = registryId;
    }
}
