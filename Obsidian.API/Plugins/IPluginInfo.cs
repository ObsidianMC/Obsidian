namespace Obsidian.API.Plugins;

public interface IPluginInfo
{
    public string Name { get; }
    public Version Version { get; }
    public string Description { get; }
    public string[] Authors { get; }
    public Uri ProjectUrl { get; }

    public PluginDependency[] Dependencies { get; }
}

public readonly struct PluginDependency
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public DependencyPriority Priority { get; init; }
}

public enum DependencyPriority
{
    Soft,
    Hard
}
