using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Codec : IHasName, IRegistryItem
{
    public string Name { get; }

    public int RegistryId { get; }

    public Dictionary<string, JsonElement> Properties { get; set; } = [];

    public Codec(string name, int registryId)
    {
        this.Name = name;
        this.RegistryId = registryId;
    }
}
