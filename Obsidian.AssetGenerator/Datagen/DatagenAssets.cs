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
        ["minecraft:data_component_type", "minecraft:map_decoration_type", "minecraft:mob_effect", "minecraft:potion"];

    // The loot table categories the server fills containers from (see LootRegistryGenerator).
    private static readonly string[] lootTableCategories = ["archaeology", "chests", "dispensers", "pots", "spawners"];

    /// <param name="generatedDirectory">The data generators' output (<c>data</c> and <c>reports</c>).</param>
    /// <param name="worldgenGroupsPath">The Java dumper's vanilla classes declaring each feature (see <see cref="WorldgenAssets"/>).</param>
    /// <param name="packetFieldsPath">The Java dumper's fields of each packet (see <see cref="JavaDumper.PacketFieldsPath"/>).</param>
    /// <param name="outputDirectory">The assets directory to write into.</param>
    public static void Write(string generatedDirectory, string worldgenGroupsPath, string packetFieldsPath, string outputDirectory)
    {
        var reports = Path.Combine(generatedDirectory, "reports");
        var data = Path.Combine(generatedDirectory, "data", "minecraft");

        File.Copy(Path.Combine(reports, "blocks.json"), Path.Combine(outputDirectory, "blocks.json"), overwrite: true);
        File.Copy(Path.Combine(reports, "items.json"), Path.Combine(outputDirectory, "item_components.json"), overwrite: true);
        CopyDirectory(Path.Combine(reports, "biome_parameters", "minecraft"), Path.Combine(outputDirectory, "biome_parameters"));
        CopyDirectory(Path.Combine(data, "worldgen", "configured_carver"), Path.Combine(outputDirectory, "configured_carver"));

        WriteRegistries(ReadObject(Path.Combine(reports, "registries.json")), outputDirectory);
        WritePackets(ReadObject(Path.Combine(reports, "packets.json")), ReadObject(packetFieldsPath), outputDirectory);

        WriteJson(Path.Combine(outputDirectory, "recipes.json"), Combine(Path.Combine(data, "recipe"), path => path));
        WriteJson(Path.Combine(outputDirectory, "advancements.json"), Combine(Path.Combine(data, "advancement"), ToId));
        WriteJson(Path.Combine(outputDirectory, "enchantments.json"), Combine(Path.Combine(data, "enchantment"), ToId));
        WriteJson(Path.Combine(outputDirectory, "dialogs.json"), Combine(Path.Combine(data, "dialog"), ToId));
        WriteJson(Path.Combine(outputDirectory, "instruments.json"), Combine(Path.Combine(data, "instrument"), ToId));

        foreach (var category in lootTableCategories)
        {
            var tables = Combine(Path.Combine(data, "loot_table", category), path => ToId($"{category}/{path}"));
            WriteJson(Path.Combine(outputDirectory, "loot_table", $"{category}.json"), tables);
        }

        WriteTags(Path.Combine(data, "tags"), outputDirectory);
        CodecAssets.Write(data, outputDirectory);
        WorldgenAssets.Write(data, reports, worldgenGroupsPath, outputDirectory);
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

        var enums = new JsonObject();
        foreach (var registry in enumRegistries)
            enums[registry] = Entries(registry).DeepClone();

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
