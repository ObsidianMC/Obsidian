namespace Obsidian.API.Plugins;

public interface IPluginInfo
{
    public string Name { get; }
    public Version Version { get; }
    public string Description { get; }
    public ImmutableArray<string> Authors { get; }
    public Uri ProjectUrl { get; }

    public ImmutableArray<PluginDependency> Dependencies { get; }
}

public readonly record struct PluginDependency
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public bool Required { get; init; }
}
