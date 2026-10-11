using Obsidian.API.Plugins;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Obsidian.Plugins;

public sealed class PluginInfo : IPluginInfo
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required Version Version { get; init; } = new Version();
    public required string AssemblyName { get; init; }

    /// <summary>
    /// The version of the plugin API the plugin was built against, or empty if its archive doesn't say.
    /// </summary>
    public string ApiVersion { get; init; } = string.Empty;

    public PluginSide Side { get; init; }

    public ImmutableArray<PluginDependency> Dependencies { get; init; } = [];
    public string Description { get; init; } = string.Empty;
    public ImmutableArray<string> Authors { get; init; } = [];
    public Uri? ProjectUrl { get; init; }

    [JsonConstructor]
    internal PluginInfo() { }

    [SetsRequiredMembers]
    internal PluginInfo(string name)
    {
        Name = name;
        Id = name;
        AssemblyName = name;
        Authors = ["Unknown"];
    }
}
