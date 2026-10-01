using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class BaseFeature
{
    public string Name { get; set; } = default!;

    public List<JsonProperty> Properties { get; set; } = [];
}
