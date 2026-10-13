using System.Text.Json.Nodes;

namespace Obsidian.Porting;

/// <summary>
/// Converts a NeoForge mod's <c>META-INF/neoforge.mods.toml</c> to Obsidian's <c>obsidian.mod.json</c>, whose fields
/// follow it where the two overlap.
/// </summary>
public static class ModMetadata
{
    // Dependencies on the loader and the game, which an Obsidian plugin doesn't declare: Obsidian is both.
    private static readonly string[] platforms = ["neoforge", "forge", "minecraft", "fml", "javafml"];

    // The fields of a [[mods]] entry that map to Obsidian's metadata.
    private static readonly string[] mappedFields =
    [
        "modId",
        "version",
        "displayName",
        "authors",
        "description",
        "displayURL"
    ];

    /// <summary>
    /// Converts a <c>neoforge.mods.toml</c>, the first mod in it if there are several.
    /// </summary>
    /// <param name="toml">The file's text.</param>
    /// <param name="jarVersion">
    /// The jar's <c>Implementation-Version</c>, which a version of <c>${file.jarVersion}</c> stands for, if known.
    /// </param>
    /// <param name="path">The file's path, for the report.</param>
    /// <param name="report">Gets what couldn't be converted and what to check.</param>
    /// <returns>The <c>obsidian.mod.json</c> contents, or null when the file has no mod.</returns>
    public static JsonObject? Convert(string toml, string? jarVersion, string path, PortingReport report)
    {
        Dictionary<string, object> root;

        try
        {
            root = Toml.Parse(toml);
        }
        catch (FormatException ex)
        {
            report.Skip(path, $"it isn't TOML this tool reads: {ex.Message}");
            return null;
        }

        if (root.GetValueOrDefault("mods") is not List<object> { Count: > 0 } mods
            || mods[0] is not Dictionary<string, object> mod
            || mod.GetValueOrDefault("modId") is not string modId)
        {
            report.Skip(path, "it declares no mod with a modId");
            return null;
        }

        if (mods.Count > 1)
        {
            report.Skip(
                path,
                $"it declares {mods.Count} mods; an Obsidian plugin is one, so only {modId} was converted");
        }

        var json = new JsonObject
        {
            ["modId"] = modId,
            ["displayName"] = mod.GetValueOrDefault("displayName") as string ?? modId,
            ["version"] = Version(mod, jarVersion, path, report),
            ["assembly"] = AssemblyName(modId)
        };

        report.Notes.Add(
            $"obsidian.mod.json names the assembly {json["assembly"]}: make it your C# plugin's assembly.");

        if (Authors(mod.GetValueOrDefault("authors")) is { Count: > 0 } authors)
            json["authors"] = new JsonArray([.. authors.Select(author => (JsonNode)author)]);

        if (mod.GetValueOrDefault("description") is string description)
            json["description"] = description.Trim();

        if (mod.GetValueOrDefault("displayURL") is string url)
            json["projectUrl"] = url;

        var dependencies = Dependencies(root, modId, path, report);
        if (dependencies.Count > 0)
            json["dependencies"] = dependencies;

        var unmapped = mod.Keys
            .Where(key => !mappedFields.Contains(key))
            .Concat(root.Keys.Where(key => key is not ("mods" or "dependencies")))
            .ToList();

        if (unmapped.Count > 0)
            report.Skip(path, $"left out, with no Obsidian equivalent: {string.Join(", ", unmapped)}");

        return json;
    }

    private static string Version(
        Dictionary<string, object> mod,
        string? jarVersion,
        string path,
        PortingReport report)
    {
        var version = mod.GetValueOrDefault("version") as string;

        if (version is not null && !version.Contains("${", StringComparison.Ordinal))
            return version;

        if (version == "${file.jarVersion}" && jarVersion is not null)
            return jarVersion;

        report.Skip(path, $"its version '{version}' is filled in by the build; 1.0.0 was written instead");

        return "1.0.0";
    }

    // NeoForge's authors are one free-form string, usually comma-separated; Obsidian's are a list.
    private static List<string> Authors(object? value) => value switch
    {
        string authors => [.. authors.Split(',', SplitOptions)],
        List<object> list => [.. list.OfType<string>()],
        _ => []
    };

    private static JsonArray Dependencies(
        Dictionary<string, object> root,
        string modId,
        string path,
        PortingReport report)
    {
        var dependencies = new JsonArray();

        if (root.GetValueOrDefault("dependencies") is not Dictionary<string, object> byMod
            || byMod.GetValueOrDefault(modId) is not List<object> declared)
        {
            return dependencies;
        }

        foreach (var entry in declared.OfType<Dictionary<string, object>>())
        {
            if (entry.GetValueOrDefault("modId") is not string id)
                continue;

            if (platforms.Contains(id))
            {
                report.Notes.Add($"The dependency on {id} was left out: Obsidian implements the game itself.");
                continue;
            }

            var dependency = new JsonObject
            {
                ["modId"] = id,
                ["type"] = DependencyType(entry)
            };

            if (entry.GetValueOrDefault("versionRange") is string range && range is not ("" or "*"))
                dependency["versionRange"] = range;

            if (entry.GetValueOrDefault("ordering") is string ordering && !ordering.Equals("none", Ignore))
                dependency["ordering"] = ordering.ToLowerInvariant();

            if (entry.GetValueOrDefault("side") is string side && !side.Equals("both", Ignore))
                dependency["side"] = side.ToLowerInvariant();

            dependencies.Add(dependency);
        }

        if (dependencies.Count > 0)
            report.Notes.Add("Dependencies keep their mod ids: check that each has an Obsidian plugin with that id.");

        return dependencies;
    }

    // NeoForge's "type", or the older "mandatory" flag.
    private static string DependencyType(Dictionary<string, object> entry) =>
        entry.GetValueOrDefault("type") is string type
            ? type.ToLowerInvariant()
            : entry.GetValueOrDefault("mandatory") is false ? "optional" : "required";

    // examplemod or example_mod becomes Examplemod or ExampleMod.
    private static string AssemblyName(string modId) => string.Concat(modId
        .Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private const StringComparison Ignore = StringComparison.OrdinalIgnoreCase;

    private const StringSplitOptions SplitOptions =
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
}
