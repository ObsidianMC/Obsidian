using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;
internal sealed class TreeFeature
{
    public string Name { get; set; } = default!;

    public List<JsonProperty> Properties { get; set; } = [];
}
