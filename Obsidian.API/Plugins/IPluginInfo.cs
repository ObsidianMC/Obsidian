namespace Obsidian.API.Plugins;

public interface IPluginInfo
{
    /// <summary>
    /// The plugin's unique id, which other plugins name in their dependencies. Like a NeoForge mod id, it is also the
    /// namespace of the content and network channels the plugin registers.
    /// </summary>
    public string Id { get; }

    public string Name { get; }
    public Version Version { get; }
    public string Description { get; }
    public ImmutableArray<string> Authors { get; }
    public Uri ProjectUrl { get; }

    /// <summary>
    /// Where players report the plugin's bugs, or null.
    /// </summary>
    public Uri? IssueTrackerUrl { get; }

    /// <summary>
    /// The path of a PNG in the plugin's archive that mod lists show as its icon, or null.
    /// </summary>
    public string? LogoFile { get; }

    /// <summary>
    /// The plugin's license as its author names it, e.g. <c>MIT</c>, or null.
    /// </summary>
    public string? License { get; }

    /// <summary>
    /// The people and projects the plugin thanks, or null.
    /// </summary>
    public string? Credits { get; }

    /// <summary>
    /// Where the plugin runs: on servers, on clients, or both.
    /// </summary>
    public PluginSide Side { get; }

    public ImmutableArray<PluginDependency> Dependencies { get; }
}

/// <summary>
/// Another plugin that a plugin depends on, modelled on a NeoForge mod dependency.
/// </summary>
public readonly record struct PluginDependency
{
    public required string Id { get; init; }

    /// <summary>
    /// The accepted versions as a NuGet or Maven version range, e.g. <c>[1.0,2.0)</c>, or a minimum version like
    /// <c>1.0</c>.
    /// </summary>
    public required string Version { get; init; }

    public PluginDependencyType Type { get; init; }

    /// <summary>
    /// Whether the dependency must be present. Shorthand for <see cref="Type"/> being
    /// <see cref="PluginDependencyType.Required"/> or <see cref="PluginDependencyType.Optional"/>.
    /// </summary>
    public bool Required
    {
        get => this.Type == PluginDependencyType.Required;
        init => this.Type = value ? PluginDependencyType.Required : PluginDependencyType.Optional;
    }

    /// <summary>
    /// Whether this plugin loads before or after the dependency. A dependency that is present always loads first
    /// unless this says <see cref="PluginDependencyOrdering.Before"/>.
    /// </summary>
    public PluginDependencyOrdering Ordering { get; init; }

    /// <summary>
    /// The side the dependency applies to; on the other side it is ignored.
    /// </summary>
    public PluginSide Side { get; init; }
}

/// <summary>
/// How a plugin relates to one it names as a dependency. Matches the dependency types of NeoForge's mod metadata.
/// </summary>
public enum PluginDependencyType
{
    /// <summary>
    /// The dependency must be present with an accepted version, or the plugin doesn't load.
    /// </summary>
    Required,

    /// <summary>
    /// The dependency doesn't have to be present, but if it is, its version must be accepted.
    /// </summary>
    Optional,

    /// <summary>
    /// The plugin doesn't load while a plugin with an accepted version is present.
    /// </summary>
    Incompatible,

    /// <summary>
    /// The plugin loads, with a warning, while a plugin with an accepted version is present.
    /// </summary>
    Discouraged
}

public enum PluginDependencyOrdering
{
    None,
    Before,
    After
}

/// <summary>
/// Where a plugin or a dependency applies, like NeoForge's <c>side</c>.
/// </summary>
public enum PluginSide
{
    Both,
    Server,
    Client
}
