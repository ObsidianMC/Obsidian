using Obsidian.API.World.Structures;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;
using Obsidian.WorldData.Structures.Pools;
using System.IO;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.WorldData.Features;

// Structures, structure sets, template pools and processor lists, read like vanilla's codecs read a data
// pack's entries, with the ranges their codecs check.
internal sealed partial class WorldgenReader
{
    // Vanilla's StructureSettings codec requires these of every structure, whatever its type.
    private static readonly string[] structureFields = ["biomes", "spawn_overrides", "step"];

    // What Obsidian reads of a structure of a plugin's type: its type and vanilla's StructureSettings.
    private static readonly string[] customStructureFields =
        ["type", "biomes", "spawn_overrides", "step", "terrain_adaptation"];

    // What Obsidian reads of a pool element of a plugin's type: its type and its projection.
    private static readonly string[] poolElementFields = ["element_type", "projection"];

    /// <summary>
    /// Reads a structure, <c>{"type": ..., "biomes": ..., "step": ..., ...}</c>.
    /// </summary>
    /// <param name="identifier">The structure's identifier.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public Structure ReadStructure(JsonElement json, string identifier, string path = "") =>
        Guard(path, () => this.ReadStructureCore(json, identifier, path));

    /// <summary>
    /// Reads a structure set, <c>{"structures": [...], "placement": {...}}</c>.
    /// </summary>
    /// <param name="identifier">The set's identifier.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public StructureSet ReadStructureSet(JsonElement json, string identifier, string path = "") =>
        Guard(path, () =>
        {
            // Vanilla's placement codec requires the salt, which Obsidian's placements default to 0.
            Required(Required(Object(json, path), path, "placement"), Field(path, "placement"), "salt");
            return (StructureSet)this.ReadObject(typeof(StructureSet), json, null, identifier, path);
        });

    /// <summary>
    /// Reads a template pool, <c>{"fallback": ..., "elements": [...]}</c>.
    /// </summary>
    /// <param name="identifier">The pool's identifier.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public StructureTemplatePool ReadTemplatePool(JsonElement json, string identifier, string path = "") =>
        Guard(path, () => (StructureTemplatePool)this.ReadObject(
            typeof(StructureTemplatePool),
            json,
            null,
            identifier,
            path));

    /// <summary>
    /// Reads a processor list, <c>{"processors": [...]}</c> or a bare list of processors.
    /// </summary>
    /// <param name="identifier">The list's identifier.</param>
    /// <param name="path">Where the value is in its file, for messages.</param>
    public StructureProcessorList ReadProcessorList(JsonElement json, string identifier, string path = "") =>
        Guard(path, () => this.ReadProcessorListCore(json, identifier, path));

    private Structure ReadStructureCore(JsonElement json, string identifier, string path)
    {
        Object(json, path);

        foreach (var field in structureFields)
            Required(json, path, field);

        var name = Reference(Required(json, path, "type"), Field(path, "type"));
        if (types.Find(name, typeof(Structure)) is Type type)
            return (Structure)this.ReadObject(type, json, "type", identifier, path);

        var custom = types.Find(name, typeof(CustomStructure))
            ?? throw Error(Field(path, "type"), $"{name} isn't a structure type");

        // A plugin's type: Obsidian's own structure reads the fields every structure has, and the plugin's the rest.
        var structure = (PluginStructure)this.ReadObject(
            typeof(PluginStructure),
            Subset(json, customStructureFields, keep: true),
            "type",
            identifier,
            path);

        structure.Definition = (CustomStructure)this.ReadObject(
            custom,
            Subset(json, customStructureFields, keep: false),
            null,
            null,
            path);

        return structure;
    }

    // A pool element of a plugin's type, or null for Obsidian's types. Like a plugin's structure, Obsidian's element
    // reads the fields every element has, and the plugin's the rest.
    private PluginPoolElement? ReadCustomPoolElement(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("element_type", out var typeName)
            || typeName.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var name = Reference(typeName, Field(path, "element_type"));
        if (types.Find(name, typeof(StructurePoolElement)) is not null
            || types.Find(name, typeof(CustomPoolElement)) is not Type custom)
        {
            return null;
        }

        var element = (PluginPoolElement)this.ReadObject(
            typeof(PluginPoolElement),
            Subset(json, poolElementFields, keep: true),
            "element_type",
            null,
            path);

        element.Element = (CustomPoolElement)this.ReadObject(
            custom,
            Subset(json, poolElementFields, keep: false),
            null,
            null,
            path);

        return element;
    }

    // The fields of an object that are among the names, or those that aren't.
    private static JsonElement Subset(JsonElement json, string[] names, bool keep)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var property in json.EnumerateObject())
            {
                if (names.Contains(property.Name) == keep)
                    property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    // The structure entries data names by identifier, or defines inline where vanilla's codecs allow.
    private object? ReadStructureValue(Type target, JsonElement json, string path)
    {
        if (target == typeof(StructureProcessorList))
            return this.ReadProcessorListCore(json, null, path);

        if (target == typeof(StructureTemplatePool))
        {
            return json.ValueKind == JsonValueKind.String
                ? Lookup(this.structures.FindPool, Reference(json, path), path)
                : this.ReadObject(target, json, null, null, path);
        }

        if (target == typeof(StructureSet))
        {
            return json.ValueKind == JsonValueKind.String
                ? Lookup(this.structures.FindSet, Reference(json, path), path)
                : this.ReadObject(target, json, null, null, path);
        }

        if (target == typeof(StructurePoolElement)
            && this.ReadCustomPoolElement(json, path) is PluginPoolElement custom)
        {
            return custom;
        }

        if (target == typeof(JigsawMaxDistance))
            return ReadMaxDistance(json, path);

        if (target == typeof(DimensionPadding))
            return ReadPadding(json, path);

        if (target == typeof(Structure))
        {
            // Vanilla's codec allows a structure inline, but starts are saved by their structure's identifier.
            if (json.ValueKind != JsonValueKind.String)
                throw Error(path, "expected a structure's identifier: starts are saved by it, so it can't be inline");

            return Lookup(this.structures.FindStructure, Reference(json, path), path);
        }

        return null;
    }

    // Vanilla's MaxDistance codec: a number from 1 to 128, both distances, or {"horizontal": 1 to 128, "vertical":
    // 1 to 4064}, whose vertical defaults to 4064.
    private static JigsawMaxDistance ReadMaxDistance(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
            return Distance(json, path, JigsawMaxDistance.MaxHorizontal);

        foreach (var property in json.EnumerateObject())
        {
            if (property.Name is not ("horizontal" or "vertical"))
                throw Error(Field(path, property.Name), "isn't a field of a max distance");
        }

        var horizontal = json.TryGetProperty("horizontal", out var value)
            ? Distance(value, Field(path, "horizontal"), JigsawMaxDistance.MaxHorizontal)
            : throw Error(Field(path, "horizontal"), "is missing");

        var vertical = json.TryGetProperty("vertical", out var verticalValue)
            ? Distance(verticalValue, Field(path, "vertical"), JigsawMaxDistance.MaxVertical)
            : JigsawMaxDistance.MaxVertical;

        return new JigsawMaxDistance(horizontal, vertical);

        static int Distance(JsonElement value, string path, int max) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var distance) && distance >= 1
            && distance <= max
                ? distance
                : throw Error(path, $"expected a number from 1 to {max}");
    }

    // Vanilla's DimensionPadding codec: a non-negative number, both paddings, or {"bottom": ..., "top": ...}, whose
    // fields default to 0, also when they aren't valid (vanilla reads them leniently).
    private static DimensionPadding ReadPadding(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var padding) && padding >= 0
                ? padding
                : throw Error(path, "expected a non-negative number or {\"bottom\": ..., \"top\": ...}");
        }

        return new DimensionPadding(Lenient("bottom"), Lenient("top"));

        int Lenient(string name) =>
            json.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var padding)
            && padding >= 0
                ? padding
                : 0;
    }

    // A processor list is an identifier, {"processors": [...]} or, as vanilla's codec allows, a bare list.
    private StructureProcessorList ReadProcessorListCore(JsonElement json, string? identifier, string path)
    {
        if (json.ValueKind == JsonValueKind.String && identifier is null)
            return Lookup(this.structures.FindProcessorList, Reference(json, path), path);

        if (json.ValueKind != JsonValueKind.Array)
        {
            var list = this.ReadObject(typeof(StructureProcessorList), json, null, identifier, path);
            return (StructureProcessorList)list;
        }

        var processors = this.ReadValue(typeof(ImmutableArray<StructureProcessor>), json, path);
        return new StructureProcessorList
        {
            Identifier = identifier ?? string.Empty,
            Processors = (ImmutableArray<StructureProcessor>)processors!
        };
    }

    // The ranges vanilla's codecs check, for the structure values Obsidian's classes don't check themselves.
    private static void CheckStructureParameters(object value, string path)
    {
        switch (value)
        {
            case JigsawStructure jigsaw:
                CheckRange(jigsaw.Size, 0, 20, path, "size");
                // Vanilla's verifyRange: terrain adaptation reaches 12 blocks beyond the pieces.
                var adaptation = jigsaw.TerrainAdaptation == TerrainAdjustment.None ? 0 : 12;
                if (jigsaw.MaxDistanceFromCenter.Horizontal + adaptation > 128)
                {
                    throw Error(
                        path,
                        "horizontal structure size including terrain adaptation must not exceed 128");
                }

                break;
            case StructurePoolEntry entry:
                CheckRange(entry.Weight, 1, 150, path, "weight");
                break;
            case StructureSetEntry entry:
                CheckRange(entry.Weight, 1, int.MaxValue, path, "weight");
                break;
            case StructureExclusionZone zone:
                CheckRange(zone.ChunkCount, 1, 16, path, "chunk_count");
                break;
            case StructurePlacement placement:
                CheckPlacement(placement, path);
                break;
        }
    }

    private static void CheckPlacement(StructurePlacement placement, string path)
    {
        CheckRange(placement.Frequency, 0, 1, path, "frequency");
        CheckRange(placement.Salt, 0, int.MaxValue, path, "salt");

        // Vanilla's Vec3i.offsetCodec(16).
        var offset = placement.LocateOffset;
        if (Math.Max(Math.Abs(offset.X), Math.Max(Math.Abs(offset.Y), Math.Abs(offset.Z))) > 16)
            throw Error(Field(path, "locate_offset"), "has a component not from -16 to 16");

        switch (placement)
        {
            case RandomSpreadStructurePlacement spread:
                CheckRange(spread.Spacing, 0, 4096, path, "spacing");
                CheckRange(spread.Separation, 0, 4096, path, "separation");

                if (spread.Spacing <= spread.Separation)
                    throw Error(path, "spacing has to be larger than separation");

                break;
            case ConcentricRingsStructurePlacement rings:
                CheckRange(rings.Distance, 0, 1023, path, "distance");
                CheckRange(rings.Spread, 0, 1023, path, "spread");
                CheckRange(rings.Count, 1, 4095, path, "count");
                break;
        }
    }
}

/// <summary>
/// How a <see cref="WorldgenReader"/> finds the structure entries data names by identifier. Each lookup throws an
/// <see cref="InvalidDataException"/> for an identifier it doesn't know.
/// </summary>
internal sealed record StructureLookups(
    Func<string, StructureProcessorList> FindProcessorList,
    Func<string, StructureTemplatePool> FindPool,
    Func<string, Structure> FindStructure,
    Func<string, StructureSet> FindSet)
{
    /// <summary>
    /// No entries: every lookup throws. Readers that may name structure entries are given their own lookups (see
    /// <see cref="Registries.PluginStructures"/>).
    /// </summary>
    public static StructureLookups None { get; } = new(
        id => throw Unknown(id, "processor list"),
        id => throw Unknown(id, "template pool"),
        id => throw Unknown(id, "structure"),
        id => throw Unknown(id, "structure set"));

    private static InvalidDataException Unknown(string identifier, string kind) =>
        new($"{identifier} isn't a {kind}");
}
