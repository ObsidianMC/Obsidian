using System.Text.Json.Nodes;
using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Writes the assets built from the vanilla data generators' output.
/// </summary>
/// <remarks>
/// The data generators write one file per registry entry; the assets combine them into one file per registry (or per
/// category, see <see cref="WorldgenAssets"/>) in the layout the source generators and the server read.
/// </remarks>
internal static class DatagenAssets
{
    // The registries of reports/registries.json written to enums.json, which become enums in Obsidian.API.
    private static readonly string[] enumRegistries =
        ["minecraft:data_component_type", "minecraft:map_decoration_type", "minecraft:mob_effect", "minecraft:potion",
         "minecraft:entity_type", "minecraft:particle_type", "minecraft:game_event"];

    // Container, block and supported entity tables evaluated by the server (see LootRegistryGenerator).
    private static readonly string[] lootTableCategories =
        ["archaeology", "blocks", "chests", "dispensers", "pots", "spawners", "entities", "gameplay"];

    /// <param name="generatedDirectory">The data generators' output (<c>data</c> and <c>reports</c>).</param>
    /// <param name="packetFieldsPath">The Java dumper's fields of each packet (see <see cref="JavaDumper.PacketFieldsPath"/>).</param>
    /// <param name="outputDirectory">The assets directory to write into.</param>
    /// <remarks>Loot tables and worldgen come from the legacy version for now (see <see cref="WriteLegacy"/>).</remarks>
    public static void Write(string generatedDirectory, string packetFieldsPath, string outputDirectory)
    {
        var reports = Path.Combine(generatedDirectory, "reports");
        var data = Path.Combine(generatedDirectory, "data", "minecraft");

        File.Copy(Path.Combine(reports, "blocks.json"), Path.Combine(outputDirectory, "blocks.json"), overwrite: true);
        // Each item's default components, { "minecraft:stone": { "components": { ... } } }, from one report per item.
        WriteJson(Path.Combine(outputDirectory, "item_components.json"),
            Combine(Path.Combine(reports, "minecraft", "components", "item"), ToId));

        WriteRegistries(ReadObject(Path.Combine(reports, "registries.json")), outputDirectory);
        WritePackets(ReadObject(Path.Combine(reports, "packets.json")), ReadObject(packetFieldsPath), outputDirectory);

        WriteJson(Path.Combine(outputDirectory, "recipes.json"), Combine(Path.Combine(data, "recipe"), path => path));
        WriteJson(Path.Combine(outputDirectory, "advancements.json"), Combine(Path.Combine(data, "advancement"), ToId));
        WriteJson(Path.Combine(outputDirectory, "enchantments.json"), Combine(Path.Combine(data, "enchantment"), ToId));
        WriteJson(Path.Combine(outputDirectory, "dialogs.json"), Combine(Path.Combine(data, "dialog"), ToId));
        WriteJson(Path.Combine(outputDirectory, "instruments.json"), Combine(Path.Combine(data, "instrument"), ToId));

        WriteTags(Path.Combine(data, "tags"), outputDirectory);
        CodecAssets.Write(data, outputDirectory);
    }

    /// <summary>
    /// Writes the loot and worldgen assets from an older vanilla version's data generators into
    /// <paramref name="outputDirectory"/> (<c>Assets/legacy-1.21.11</c>), in the layout the current version's would have:
    /// <c>loot_table</c>, <c>worldgen</c> (see <see cref="WorldgenAssets"/>), <c>configured_carver</c>,
    /// <c>biome_parameters</c> and <c>biome_spawns.json</c>.
    /// </summary>
    /// <remarks>
    /// Temporary: the current version's loot and worldgen data don't fit Obsidian's models yet, so those systems run on
    /// the older version's. Phase 4 (loot) and Phase 5 (worldgen) of <c>docs/porting-26.3-plan.md</c> move them to the
    /// current version and remove this. LegacyTags keeps worldgen tag references (including the worldgen helpers' tags)
    /// in the temporary obsidian_legacy namespace, with legacy contents and current registry ids, excluded from client
    /// sync. Remove that bridge with LegacyMinecraftVersion when both phases are complete.
    /// </remarks>
    /// <param name="generatedDirectory">The legacy data generators' output (<c>data</c> and <c>reports</c>).</param>
    /// <param name="worldgenGroupsPath">
    /// The Java dumper's vanilla classes declaring each feature, of the current version: the legacy jar is obfuscated,
    /// so the dumper can't read it (see <see cref="WorldgenAssets"/>).
    /// </param>
    public static void WriteLegacy(string generatedDirectory, string worldgenGroupsPath,
        string outputDirectory)
    {
        var reports = Path.Combine(generatedDirectory, "reports");
        var data = Path.Combine(generatedDirectory, "data", "minecraft");

        CopyDirectory(Path.Combine(reports, "biome_parameters", "minecraft"), Path.Combine(outputDirectory, "biome_parameters"));

        foreach (var (path, carver) in ReadAll(Path.Combine(data, "worldgen", "configured_carver")))
            WriteJson(Path.Combine(outputDirectory, "configured_carver", path + ".json"), carver);

        foreach (var category in lootTableCategories)
        {
            var tables = Combine(Path.Combine(data, "loot_table", category), path => ToId($"{category}/{path}"));
            if (category == "entities")
            {
                // These need entity predicates that the evaluator does not model yet. Keep their old drops until
                // their behaviour can be checked against vanilla; do not weaken predicates to include them.
                foreach (var id in new[] { "ghast", "magma_cube", "slime", "turtle", "sheep", "pillager" })
                    tables.Remove($"minecraft:entities/{id}");
            }

            if (category == "gameplay")
            {
                // Only fishing is evaluated so far (see FishingHook); the other gameplay tables need their own owners.
                foreach (var id in tables.Where(table => !table.Key.StartsWith("minecraft:gameplay/fishing", StringComparison.Ordinal)).Select(table => table.Key).ToList())
                    tables.Remove(id);
            }

            WriteJson(Path.Combine(outputDirectory, "loot_table", $"{category}.json"), tables);
        }

        // Each biome's mob spawn settings, which 26.1 moved out of the biome: { "value": [{ "name", "element" }] } in the
        // biomes' order, like the biome codec they used to be read from.
        var spawns = new JsonArray();
        foreach (var (path, biome) in ReadAll(Path.Combine(data, "worldgen", "biome")))
        {
            var element = new JsonObject();
            foreach (var property in new[] { "creature_spawn_probability", "spawners", "spawn_costs" })
            {
                if (biome[property] is { } value)
                    element[property] = value.DeepClone();
            }

            spawns.Add(new JsonObject { ["name"] = ToId(path), ["element"] = element });
        }

        WriteJson(Path.Combine(outputDirectory, "biome_spawns.json"), new JsonObject { ["value"] = spawns });

        WorldgenAssets.Write(data, reports, worldgenGroupsPath, outputDirectory);
        LegacyTags.Write(data, outputDirectory);
    }

    /// <summary>A resource location of the <c>minecraft</c> namespace from its path.</summary>
    public static string ToId(string path) => $"minecraft:{path}";

    private static void WriteRegistries(JsonObject registries, string outputDirectory)
    {
        JsonObject Entries(string registry) => registries[registry]!["entries"]!.AsObject();

        // Fluids and argument types map straight to their protocol id.
        JsonObject ProtocolIds(string registry) =>
            new(Entries(registry).Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)entry.Value!["protocol_id"]!.DeepClone())));

        WriteJson(Path.Combine(outputDirectory, "items.json"), Entries("minecraft:item"));
        WriteJson(Path.Combine(outputDirectory, "sounds.json"), Entries("minecraft:sound_event"));
        WriteJson(Path.Combine(outputDirectory, "fluids.json"), ProtocolIds("minecraft:fluid"));
        WriteJson(Path.Combine(outputDirectory, "command_parsers.json"), ProtocolIds("minecraft:command_argument_type"));

        // Every built-in registry's entries in protocol id order, which the Obsidian client checks a NeoForge server's
        // registry sync against.
        var registryIds = new JsonObject();
        foreach (var (registry, _) in registries)
        {
            var names = Entries(registry)
                .OrderBy(entry => entry.Value!["protocol_id"]!.GetValue<int>())
                .Select(entry => (JsonNode?)entry.Key);

            registryIds[registry] = new JsonArray([.. names]);
        }

        WriteJson(Path.Combine(outputDirectory, "registry_ids.json"), registryIds);

        var enums = new JsonObject();
        foreach (var registry in enumRegistries)
            enums[registry] = Entries(registry).DeepClone();

        // Feed the dumper's attribute ids through the same asset as the API's other protocol-valued types.
        enums["minecraft:attribute"] = new JsonObject(ReadObject(Path.Combine(outputDirectory, "attribute_ids.json"))
            .Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)new JsonObject
            {
                ["protocol_id"] = entry.Value!.DeepClone()
            })));

        WriteJson(Path.Combine(outputDirectory, "enums.json"), enums);
    }

    /// <summary>
    /// Flattens the packets report (<c>state → direction → packet → protocol_id</c>) into the list the packet source
    /// generator reads, with the Java dumper's fields of each packet.
    /// </summary>
    private static void WritePackets(JsonObject report, JsonObject fields, string outputDirectory)
    {
        var packets = new JsonArray();
        foreach (var (state, directions) in report)
        {
            foreach (var (direction, statePackets) in directions!.AsObject())
            {
                foreach (var (id, packet) in statePackets!.AsObject())
                {
                    var resourceId = id[(id.IndexOf(':') + 1)..];
                    packets.Add(new JsonObject
                    {
                        ["name"] = ToPascalCase(resourceId),
                        ["resource_id"] = resourceId,
                        ["namespace"] = ToPascalCase(direction),
                        ["state"] = ToPascalCase(state),
                        ["packet_id"] = packet!["protocol_id"]!.DeepClone(),
                        ["usable_interface"] = $"I{ToPascalCase(direction)}Packet",
                        ["fields"] = (fields[direction]?[id]
                            ?? throw new InvalidDataException($"The Java dumper has no fields for {direction} {id}.")).DeepClone()
                    });
                }
            }
        }

        WriteJson(Path.Combine(outputDirectory, "packets.json"), packets);
    }

    /// <summary>
    /// Writes every tag as <c>"block/logs": { "name": "logs", "type": "block", "values": [...] }</c>, where the type is
    /// the tag's registry folder (<c>worldgen/biome</c>, <c>item/enchantable</c>).
    /// </summary>
    private static void WriteTags(string tagsDirectory, string outputDirectory)
    {
        var tags = new JsonObject();
        foreach (var (path, tag) in ReadAll(tagsDirectory))
        {
            var separator = path.LastIndexOf('/');
            tags[path] = new JsonObject
            {
                ["name"] = path[(separator + 1)..],
                ["type"] = path[..separator],
                ["values"] = tag["values"]!.DeepClone()
            };
        }

        WriteJson(Path.Combine(outputDirectory, "tags.json"), tags);
    }

    // clientbound → Clientbound, set_border_lerp_size → SetBorderLerpSize, debug/block_value → DebugBlockValue.
    private static string ToPascalCase(string path) =>
        string.Concat(path.Split(['_', '/'], StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
