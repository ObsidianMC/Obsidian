using Obsidian.API.Registries;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Obsidian.API.Inventory.DataComponents;

// Disk codecs name registry entries, never process or connection IDs. Registered plugin identities stay available
// across unloads, like the registries themselves; no plugin code is retained here.
internal static class ComponentRegistryNames
{
    private static readonly Lazy<Dictionary<string, string[]>> vanilla = new(Load);
    private static readonly ConcurrentDictionary<(string Registry, int Id), string> registered = new();

    public static void Register(string registry, int id, string name) => registered[(registry, id)] = name;

    public static string Name(string registry, int id)
    {
        if (registered.TryGetValue((registry, id), out var name))
            return name;

        if (registry == ComponentValueCodecs.Items && ItemsRegistry.TryGet(id, out var item))
            return item.UnlocalizedName;

        if (registry == ComponentValueCodecs.DataComponentTypes)
            return OpaqueDataComponent.GetIdentifier((DataComponentType)id);

        if (registry == "minecraft:instrument" && (uint)id < (uint)InstrumentsRegistry.All.Count)
            return InstrumentsRegistry.All[id].Identifier;

        if (registry == "minecraft:trim_material" && CodecRegistry.TrimMaterial.ById.TryGetValue(id, out var material))
            return material.Name;

        if (registry == "minecraft:trim_pattern" && CodecRegistry.TrimPattern.ById.TryGetValue(id, out var pattern))
            return pattern.Name;

        if (vanilla.Value.TryGetValue(registry, out var names) && (uint)id < (uint)names.Length)
            return names[id];

        throw new NotSupportedException($"Cannot save {registry} id {id}: its identifier is unavailable.");
    }

    public static int Id(string registry, string name)
    {
        if (!name.Contains(':'))
            name = "minecraft:" + name;

        if (registry == ComponentValueCodecs.Items && ItemsRegistry.TryGet(name, out var item))
            return item.Id;

        if (registry == ComponentValueCodecs.DataComponentTypes)
        {
            if (DataComponentTypesRegistry.TryGet(name, out var plugin))
                return (int)plugin.Type;

            foreach (var type in Enum.GetValues<DataComponentType>())
            {
                if (OpaqueDataComponent.GetIdentifier(type) == name)
                    return (int)type;
            }
        }

        if (registry == "minecraft:instrument")
        {
            var instrument = InstrumentsRegistry.All.FirstOrDefault(value => value.Identifier == name);
            if (instrument is not null)
                return instrument.Id;
        }

        if (registry == "minecraft:trim_material"
            && CodecRegistry.TrimMaterial.All.TryGetValue(name, out var material))
        {
            return material.Id;
        }

        if (registry == "minecraft:trim_pattern" && CodecRegistry.TrimPattern.All.TryGetValue(name, out var pattern))
            return pattern.Id;

        foreach (var (key, value) in registered)
        {
            if (key.Registry == registry && value == name)
                return key.Id;
        }

        if (vanilla.Value.TryGetValue(registry, out var names) && Array.IndexOf(names, name) is >= 0 and var id)
            return id;

        throw new InvalidDataException($"Unknown {registry} entry {name}.");
    }

    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(ComponentRegistryNames).Assembly
            .GetManifestResourceStream("Obsidian.API.registry_ids.json")!;
        var names = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
        using var components = typeof(ComponentRegistryNames).Assembly
            .GetManifestResourceStream("Obsidian.API.component_registry_ids.json")!;
        foreach (var (registry, values) in JsonSerializer.Deserialize<Dictionary<string, string[]>>(components)!)
            names.Add(registry, values);

        using var syncedStream = typeof(ComponentRegistryNames).Assembly
            .GetManifestResourceStream("Obsidian.API.synced_registries.json")!;
        using var synced = JsonDocument.Parse(syncedStream);
        foreach (var registry in synced.RootElement.EnumerateArray())
        {
            var identifier = registry.GetProperty("registry").GetString()!;
            var entries = registry.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetString()!)
                .ToArray();
            names[identifier] = entries;
        }

        return names;
    }
}
