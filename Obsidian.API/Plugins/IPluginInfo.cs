namespace Obsidian.API.Plugins;

/// <summary>
/// A plugin's metadata, read from its archive.
/// </summary>
public interface IPluginInfo
{
    /// <summary>
    /// The plugin's unique id, which other plugins name in their dependencies. Like a NeoForge mod id, it is also the
    /// namespace of the content and network channels the plugin registers.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the plugin's display name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the plugin's version, which other plugins' dependency version ranges are checked against.
    /// </summary>
    public Version Version { get; }
    /// <summary>
    /// Gets a short description of the plugin.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// Gets the names of the plugin's authors.
    /// </summary>
    public ImmutableArray<string> Authors { get; }
    /// <summary>
    /// Gets the plugin's project page.
    /// </summary>
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

    /// <summary>
    /// Gets the other plugins this plugin depends on, is incompatible with, or orders itself against.
    /// </summary>
    public ImmutableArray<PluginDependency> Dependencies { get; }
}

/// <summary>
/// Another plugin that a plugin depends on, modelled on a NeoForge mod dependency.
/// </summary>
public readonly record struct PluginDependency
{
    /// <summary>
    /// Gets the <see cref="IPluginInfo.Id"/> of the plugin depended on.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The accepted versions as a NuGet or Maven version range, e.g. <c>[1.0,2.0)</c>, or a minimum version like
    /// <c>1.0</c>.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets how the plugin relates to the dependency. Defaults to <see cref="PluginDependencyType.Required"/>.
    /// </summary>
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

/// <summary>
/// The load order a plugin asks for relative to one of its dependencies.
/// </summary>
public enum PluginDependencyOrdering
{
    /// <summary>
    /// No order is given: a present dependency loads first, unless that contradicts other orderings.
    /// </summary>
    None,
    /// <summary>
    /// The plugin loads before the dependency.
    /// </summary>
    Before,
    /// <summary>
    /// The plugin loads after the dependency.
    /// </summary>
    After
}

/// <summary>
/// Where a plugin or a dependency applies, like NeoForge's <c>side</c>.
/// </summary>
public enum PluginSide
{
    /// <summary>
    /// Applies on both servers and clients.
    /// </summary>
    Both,
    /// <summary>
    /// Applies only on servers.
    /// </summary>
    Server,
    /// <summary>
    /// Applies only on clients.
    /// </summary>
    Client
}
