using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class Codec(string name, int registryId) : IHasName, IRegistryItem
{
    public string Name { get; } = name;

    public int RegistryId { get; } = registryId;

    public Dictionary<string, JsonElement> Properties { get; set; } = [];
}
