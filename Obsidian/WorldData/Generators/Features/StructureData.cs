using Obsidian.API.World.Features;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang.Features;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using LegacyPlacedFeature = Obsidian.API.World.Features.PlacedFeature;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>
/// The 26.3 structures, structure sets, template pools and processor lists, read from <see cref="WorldgenData"/> into
/// the objects the legacy generator's compiled registries hold, for the 26.3 pipeline beside the live generator.
/// </summary>
/// <remarks>
/// 26.3 changed only how processors write block states (bare ids and <c>id</c>/<c>properties</c>) and how spawn
/// overrides write their counts, so entries go through <see cref="WorldgenReader"/> once their block states are in
/// the shape it reads. Pool features are placed by the 26.3 <see cref="FeatureRegistry"/>.
/// </remarks>
internal static class StructureData
{
    /// <summary>
    /// Every 26.3 structure and structure set, and the pools their jigsaws can name.
    /// </summary>
    public static WorldgenStructures Read(FeatureRegistry features)
    {
        var data = features.Data;
        var lists = new Dictionary<string, StructureProcessorList>();
        var pools = new Dictionary<string, StructureTemplatePool>();
        var structures = new Dictionary<string, Structure>();
        var sets = new Dictionary<string, StructureSet>();
        WorldgenReader? reader = null;

        var lookups = new StructureLookups(
            id => Find(lists, id, () => reader!.ReadProcessorList(Entry(data, "processor_list", id), id)),
            id => Find(pools, id, () => reader!.ReadTemplatePool(Entry(data, "template_pool", id), id)),
            id => Find(structures, id, () => reader!.ReadStructure(Entry(data, "structure", id), id)),
            id => Find(sets, id, () => reader!.ReadStructureSet(Entry(data, "structure_set", id), id)));

        reader = new WorldgenReader(
            WorldgenTypes.BuiltIn,
            id => throw new InvalidDataException($"A 26.3 structure names the configured feature {id}."),
            id => PoolFeature(features, id),
            structures: lookups);

        foreach (var id in data.Ids("structure"))
        {
            lookups.FindStructure(id);
        }

        foreach (var id in data.Ids("structure_set"))
        {
            lookups.FindSet(id);
        }

        // Jigsaw blocks name pools by id as pieces are placed, so every pool is read now.
        foreach (var id in data.Ids("template_pool"))
        {
            lookups.FindPool(id);
        }

        foreach (var pool in pools.Values)
        {
            foreach (var entry in pool.Elements)
            {
                UnnameFeatureJigsaws(entry.Element);
            }
        }

        return new WorldgenStructures
        {
            Version = 0,
            Structures = structures.ToFrozenDictionary(),
            Sets = sets.ToFrozenDictionary(),
            Pools = pools.ToFrozenDictionary(),
            Templates = FrozenDictionary<string, StructureTemplate>.Empty,
            Owners = FrozenDictionary<Structure, ImmutableArray<PluginContainer>>.Empty,
            LegacyPools = false
        };
    }

    // 26.3's feature elements have a jigsaw without a name.
    private static void UnnameFeatureJigsaws(StructurePoolElement element)
    {
        if (element is FeaturePoolElement feature)
        {
            feature.UnnamedJigsaw = true;
        }
        else if (element is ListPoolElement list)
        {
            foreach (var child in list.Elements)
            {
                UnnameFeatureJigsaws(child);
            }
        }
    }

    // Entries refer to each other by id, possibly before they are read in turn.
    private static T Find<T>(Dictionary<string, T> entries, string id, Func<T> read)
    {
        id = WorldgenData.Qualify(id);
        if (entries.TryGetValue(id, out var entry))
            return entry;

        entry = read();
        entries[id] = entry;
        return entry;
    }

    private static JsonElement Entry(WorldgenData data, string registry, string id) =>
        FeatureRegistry.TemplateProcessorStates(data.Read(registry, id));

    // A feature pool element's placed feature, placed by the 26.3 registry with no placement of its own.
    private static LegacyPlacedFeature PoolFeature(FeatureRegistry features, string id) => new()
    {
        Identifier = WorldgenData.Qualify(id),
        Feature = new RegistryFeature(features, WorldgenData.Qualify(id)),
        Placement = []
    };

    private sealed class RegistryFeature(FeatureRegistry features, string id) : ConfiguredFeatureBase
    {
        public override string Type => "obsidian:feature_registry";

        public override bool Place(FeatureContext context)
        {
            var region = (WorldGenRegion)context.Level;
            var decoration = new DecorationContext(
                features,
                region,
                position => region.GetBiome(position).Name,
                context.Generation.MinY,
                context.Generation.Height);

            return features.Placed(id).Place(decoration, context.Random, context.Origin);
        }
    }
}
