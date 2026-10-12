using Obsidian.API.World.Structures;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;
using Obsidian.WorldData.Structures.Pools;
using System.IO;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.WorldData.Features;

// Structures, structure sets, template pools and processor lists, read like vanilla 1.21.11's codecs read a data
// pack's entries, with the ranges their codecs check.
internal sealed partial class WorldgenReader
{
    // Vanilla's StructureSettings codec requires these of every structure, whatever its type.
    private static readonly string[] structureFields = ["biomes", "spawn_overrides", "step"];

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
        var type = types.Find(name, typeof(Structure))
            ?? throw Error(Field(path, "type"), $"{name} isn't a structure type");

        return (Structure)this.ReadObject(type, json, "type", identifier, path);
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

        if (target == typeof(Structure))
        {
            // Vanilla's codec allows a structure inline, but starts are saved by their structure's identifier.
            if (json.ValueKind != JsonValueKind.String)
                throw Error(path, "expected a structure's identifier: starts are saved by it, so it can't be inline");

            return Lookup(this.structures.FindStructure, Reference(json, path), path);
        }

        return null;
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
                CheckRange(jigsaw.MaxDistanceFromCenter, 1, 128, path, "max_distance_from_center");

                if (jigsaw.DimensionPadding < 0)
                    throw Error(Field(path, "dimension_padding"), $"{jigsaw.DimensionPadding} is negative");

                // Vanilla's verifyRange: terrain adaptation reaches 12 blocks beyond the pieces.
                var adaptation = jigsaw.TerrainAdaptation == TerrainAdjustment.None ? 0 : 12;
                if (jigsaw.MaxDistanceFromCenter + adaptation > 128)
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
    /// Vanilla's compiled entries.
    /// </summary>
    public static StructureLookups Vanilla { get; } = new(
        id => Find(ProcessorLists.All, id, "processor list"),
        id => Find(TemplatePools.All, id, "template pool"),
        id => Find(global::Obsidian.Registries.Structures.All, id, "structure"),
        id => Find(StructureSets.All, id, "structure set"));

    private static T Find<T>(IReadOnlyDictionary<string, T> entries, string identifier, string kind) =>
        entries.TryGetValue(identifier, out var entry)
            ? entry
            : throw new InvalidDataException($"{identifier} isn't a {kind}");
}
