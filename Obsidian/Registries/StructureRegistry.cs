using Obsidian.WorldData.Structures;
using System.Reflection;

namespace Obsidian.Registries;

/// <summary>
/// Structure templates embedded under <c>Assets/Structures</c>, by id: <c>minecraft:fossil/spine_1</c> is
/// <c>Assets/Structures/fossil/spine_1.nbt</c>.
/// </summary>
internal static class StructureRegistry
{
    private const string ResourcePrefix = "Obsidian.Assets.Structures.";
    private const string ResourceSuffix = ".nbt";

    private static readonly ConcurrentDictionary<string, StructureTemplate> templates = new();

    /// <summary>Loads every embedded template up front.</summary>
    public static void Initialize()
    {
        foreach (var resource in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                var path = resource[ResourcePrefix.Length..^ResourceSuffix.Length].Replace('.', '/');
                Get("minecraft:" + path);
            }
        }
    }

    /// <summary>Gets a template by id, loading it on first use; unknown ids get an empty template.</summary>
    public static StructureTemplate Get(string id) => templates.GetOrAdd(id, Load);

    private static StructureTemplate Load(string id)
    {
        var path = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id["minecraft:".Length..] : id;
        var resource = ResourcePrefix + path.Replace('/', '.') + ResourceSuffix;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);

        // Like vanilla's StructureTemplateManager.getOrCreate, an unknown id is an empty template (one vanilla pool
        // references a template that doesn't exist).
        return stream is null ? StructureTemplate.CreateEmpty() : StructureTemplate.Load(stream);
    }
}
