using NuGet.Versioning;
using Obsidian.API.Plugins;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Plugins.Archives;

/// <summary>
/// The contents of <c>obsidian.mod.json</c>, the metadata file at the root of a version 2 plugin archive. Its fields
/// follow NeoForge's <c>neoforge.mods.toml</c> where the two overlap (mod id, version ranges, dependency type, ordering
/// and side), written as JSON. See docs/plugin-archives.md.
/// </summary>
internal sealed class PluginMetadata
{
    public const string FileName = "obsidian.mod.json";

    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public required string ModId { get; init; }

    public required string Version { get; init; }

    /// <summary>
    /// The plugin's main assembly, without the <c>.dll</c> extension.
    /// </summary>
    public required string Assembly { get; init; }

    public string? DisplayName { get; init; }

    public string ApiVersion { get; init; } = string.Empty;

    public string[] Authors { get; init; } = [];

    public string Description { get; init; } = string.Empty;

    public string? ProjectUrl { get; init; }

    public PluginSide Side { get; init; }

    public DependencyMetadata[] Dependencies { get; init; } = [];

    public static PluginMetadata Parse(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<PluginMetadata>(json, options)
        ?? throw new InvalidDataException($"{FileName} is empty.");

    public PluginInfo ToPluginInfo()
    {
        // Semantic versions like 1.2.0-beta are accepted; the plugin's version drops the prerelease label.
        if (!NuGetVersion.TryParse(this.Version, out var version))
            throw new InvalidDataException($"{FileName} has an invalid version '{this.Version}'.");

        return new PluginInfo
        {
            Id = this.ModId,
            Name = this.DisplayName ?? this.ModId,
            Version = version.Version,
            AssemblyName = this.Assembly,
            ApiVersion = this.ApiVersion,
            Authors = [.. this.Authors],
            Description = this.Description,
            ProjectUrl = Uri.TryCreate(this.ProjectUrl, UriKind.Absolute, out var uri) ? uri : null,
            Side = this.Side,
            Dependencies = [.. this.Dependencies.Select(dependency => dependency.ToDependency())]
        };
    }

    public sealed class DependencyMetadata
    {
        public required string ModId { get; init; }

        public PluginDependencyType Type { get; init; }

        /// <summary>
        /// The accepted versions; any version when left out.
        /// </summary>
        public string VersionRange { get; init; } = "0.0";

        public PluginDependencyOrdering Ordering { get; init; }

        public PluginSide Side { get; init; }

        public PluginDependency ToDependency() => new()
        {
            Id = this.ModId,
            Version = this.VersionRange,
            Type = this.Type,
            Ordering = this.Ordering,
            Side = this.Side
        };
    }
}
