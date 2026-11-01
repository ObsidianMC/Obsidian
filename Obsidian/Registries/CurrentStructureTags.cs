using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>The current data pack's structure tags, before plugin overrides.</summary>
internal static class CurrentStructureTags
{
    public static FrozenDictionary<string, IReadOnlySet<string>> All { get; } = Read();

    private static FrozenDictionary<string, IReadOnlySet<string>> Read()
    {
        using var stream = typeof(CurrentStructureTags).Assembly.GetManifestResourceStream("Obsidian.Assets.tags.json")!;
        using var document = JsonDocument.Parse(stream);
        const string prefix = "worldgen/structure/";
        var definitions = document.RootElement.EnumerateObject().Where(entry => entry.Name.StartsWith(prefix))
            .ToDictionary(entry => $"minecraft:{entry.Name[prefix.Length..]}",
                entry => entry.Value.GetProperty("values").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray());
        var result = new Dictionary<string, IReadOnlySet<string>>();
        foreach (var name in definitions.Keys)
            Resolve(name);
        return result.ToFrozenDictionary();

        IReadOnlySet<string> Resolve(string name)
        {
            if (result.TryGetValue(name, out var found))
                return found;
            var values = new HashSet<string>();
            foreach (var value in definitions[name])
            {
                if (value.StartsWith('#'))
                    values.UnionWith(Resolve(value[1..]));
                else
                    values.Add(value);
            }
            return result[name] = values;
        }
    }
}
