using Obsidian.API.World.Features;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Terrain.Carvers;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Frozen;
using System.Threading;
using LegacyPlacedFeature = Obsidian.API.World.Features.PlacedFeature;
using LegacyPlacementContext = Obsidian.API.World.Features.PlacementContext;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private readonly Lock compiling = new();
    private bool pluginLookups;
    private readonly Dictionary<(string Registry, string Id), JsonElement> pluginEntries = [];
    private readonly HashSet<(string Registry, string Id)> reading = [];
    private readonly HashSet<(string Registry, string Id)> failed = [];
    private readonly WorldgenDependencies<PluginContainer> dependencies = new();
    private readonly Dictionary<string, ConfiguredFeatureBase> configuredAdapters = [];
    private readonly Dictionary<Feature, ConfiguredFeatureBase> pluginFeatures =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, LegacyPlacedFeature> placedAdapters = [];
    private readonly Dictionary<string, StructureProcessorList> processors = [];
    private int inlineIds;
    private WorldgenReader? reader;

    internal WorldgenTypes Types { get; private set; } = WorldgenTypes.BuiltIn;

    internal bool UsesPluginFiles => this.pluginLookups;

    /// <summary>The shared plant-growth and structure interface, using 26.3 lookups.</summary>
    public WorldgenFeatureSet? FeatureSet { get; internal set; }

    internal CarverRegistry Carvers { get; set; } = null!;

    internal IReadOnlyDictionary<object, ImmutableArray<PluginContainer>> EntryOwners => this.dependencies.Of;

    internal WorldgenFeatureSet AsFeatureSet() => this.FeatureSet ?? new WorldgenFeatureSet(
        FrozenDictionary<string, ConfiguredFeatureBase>.Empty,
        FrozenDictionary<string, LegacyPlacedFeature>.Empty,
        FrozenDictionary<string, IReadOnlyList<IReadOnlyList<LegacyPlacedFeature>>>.Empty,
        FrozenDictionary<LegacyPlacedFeature, ImmutableArray<PluginContainer>>.Empty)
    {
        ConfiguredLookup = this.FindConfigured,
        PlacedLookup = this.FindPlaced,
        ProcessorLookup = this.Processors,
        OwnerLookup = this.OwnersOf,
        InlineConfiguredLookup = this.ConfiguredAdapter,
        InlinePlacedLookup = this.PlacedAdapter
    };

    internal void ConfigurePlugins(IEnumerable<FeatureFile> files, IEnumerable<WorldgenTypeRegistration> types)
    {
        this.pluginLookups = true;
        this.Types = WorldgenTypes.With(types);

        foreach (var file in files)
        {
            var registry = file.Registry switch
            {
                PluginFeatures.ConfiguredFeature or PluginFeatures.Feature => "feature",
                PluginFeatures.PlacedFeature => "placed_feature",
                PluginFeatures.ProcessorList => "processor_list",
                _ => null
            };

            if (registry is not null)
                this.pluginEntries[(registry, file.Identifier)] = file.Json;
        }
    }

    private WorldgenReader Reader => this.reader ??= new WorldgenReader(
        this.Types,
        this.ConfiguredAdapter,
        this.PlacedAdapter,
        type =>
        {
            if (WorldgenDependencies<PluginContainer>.Owner(type) is PluginContainer owner)
                this.dependencies.Add(owner);
        },
        structures: StructureLookups.None with { FindProcessorList = this.Processors },
        inlineConfiguredFeature: this.ConfiguredAdapter,
        inlinePlacedFeature: this.PlacedAdapter);

    private JsonElement Entry(string registry, string id) =>
        !this.failed.Contains((registry, id)) && this.pluginEntries.TryGetValue((registry, id), out var json)
            ? json
            : this.Data.Read(registry, id);

    // The shared dependency tracker propagates owners through named and inline children. Failed files and cycles
    // are isolated to their dependents; an invalid override leaves the vanilla entry available on the next lookup.
    private T Resolve<T>(string registry, string id, Func<T> read) where T : class
    {
        var key = (registry, id);
        if (this.failed.Contains(key) && !this.Data.Ids(registry).Contains(id))
            throw new InvalidDataException($"{id} isn't a valid {registry}.");

        if (!this.reading.Add(key))
            throw new InvalidDataException($"{id} refers back to itself.");

        try
        {
            return this.dependencies.Used(this.dependencies.Track(read));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            this.failed.Add(key);
            throw new InvalidDataException($"{registry} {id}: {ex.Message}", ex);
        }
        finally
        {
            this.reading.Remove(key);
        }
    }

    internal ConfiguredFeatureBase ConfiguredAdapter(string id)
    {
        lock (this.compiling)
            return this.ConfiguredAdapterCore(id);
    }

    internal ConfiguredFeatureBase ConfiguredAdapter(JsonElement json)
    {
        lock (this.compiling)
        {
            var feature = this.Feature(json);
            var adapter = this.pluginFeatures.GetValueOrDefault(feature) ?? new RegistryFeature(this, feature);
            this.CopyOwners(feature, adapter);
            return this.dependencies.Used(adapter);
        }
    }

    private ConfiguredFeatureBase ConfiguredAdapterCore(string id)
    {
        id = WorldgenData.Qualify(id);
        if (!this.configuredAdapters.TryGetValue(id, out var adapter))
        {
            var feature = this.Feature(JsonSerializer.SerializeToElement(id));
            adapter = this.pluginFeatures.GetValueOrDefault(feature)
                ?? new RegistryFeature(this, feature) { Identifier = id };
            this.CopyOwners(feature, adapter);
            this.configuredAdapters.Add(id, adapter);
        }

        return this.dependencies.Used(adapter);
    }

    internal ConfiguredFeatureBase? FindConfigured(string id)
    {
        try
        {
            return this.ConfiguredAdapter(id);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    internal LegacyPlacedFeature? FindPlaced(string id)
    {
        try
        {
            return this.PlacedAdapter(id);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    internal LegacyPlacedFeature PlacedAdapter(string id)
    {
        lock (this.compiling)
            return this.PlacedAdapterCore(id);
    }

    private LegacyPlacedFeature PlacedAdapterCore(string id)
    {
        id = WorldgenData.Qualify(id);
        if (!this.placedAdapters.TryGetValue(id, out var adapter))
        {
            adapter = this.PlacedAdapter(this.Placed(id));
            this.placedAdapters.Add(id, adapter);
        }

        return this.dependencies.Used(adapter);
    }

    internal LegacyPlacedFeature PlacedAdapter(JsonElement json)
    {
        lock (this.compiling)
            return this.InlinePlacedAdapter(json);
    }

    private LegacyPlacedFeature InlinePlacedAdapter(JsonElement json)
    {
        var id = $"obsidian:inline/{this.inlineIds++}";
        var placed = this.dependencies.Used(this.dependencies.Track(() => this.CompilePlaced(json, id)));
        this.placedFeatures.Add(id, placed);
        var adapter = this.PlacedAdapter(placed);
        FeaturePoolElement.InlineDefinitions.AddOrUpdate(adapter, json.GetRawText());
        return adapter;
    }

    internal LegacyPlacedFeature PlacedAdapter(PlacedFeature placed)
    {
        lock (this.compiling)
            return this.PlacedAdapterCore(placed);
    }

    private LegacyPlacedFeature PlacedAdapterCore(PlacedFeature placed)
    {
        if (placed.Id is string id && this.placedAdapters.TryGetValue(id, out var cached))
            return cached;

        Feature place = (context, random, origin) => placed.Place(context, random, origin);
        var adapter = new LegacyPlacedFeature
        {
            Identifier = placed.Id ?? string.Empty,
            Feature = new RegistryFeature(this, place),
            Placement = []
        };
        this.CopyOwners(placed, adapter);
        return adapter;
    }

    private void CopyOwners(object source, object target)
    {
        if (this.dependencies.Of.TryGetValue(source, out var owners))
            this.dependencies.Of[target] = owners;
    }

    internal StructureProcessorList Processors(string id)
    {
        lock (this.compiling)
            return this.ProcessorsCore(id);
    }

    private StructureProcessorList ProcessorsCore(string id)
    {
        id = WorldgenData.Qualify(id);
        if (!this.processors.TryGetValue(id, out var list))
        {
            list = this.Resolve("processor_list", id, () => this.ReadProcessors(this.Entry("processor_list", id), id));
            this.processors.Add(id, list);
        }

        return this.dependencies.Used(list);
    }

    private StructureProcessorList ReadProcessors(JsonElement json, string id = "") =>
        this.Reader.ReadProcessorList(TemplateProcessorStates(json), id);

    private Feature PluginFeature(JsonElement json, string? id)
    {
        // Plugin classes keep the public config/property model. 26.3 files put those properties at the root;
        // configured_feature files retain their old spelling and envelope for source compatibility.
        var fields = JsonNode.Parse(json.GetRawText())!.AsObject();
        var type = fields["type"]!.DeepClone();
        fields.Remove("type");
        var wrapped = JsonSerializer.SerializeToElement(new JsonObject { ["type"] = type, ["config"] = fields });
        var feature = this.Reader.ReadConfiguredFeature(TemplateProcessorStates(wrapped), id);

        Feature place = (context, random, origin) => feature.Place(new FeatureContext
        {
            Level = context.Level,
            Generation = new WorldGenerationContext(context.GeneratorMinY, context.GeneratorHeight),
            Random = random,
            Origin = origin,
            TopFeature = context.TopFeature
        });
        this.pluginFeatures.Add(place, feature);
        return place;
    }

    private PlacementModifier PluginModifier(JsonElement json)
    {
        var modifier = this.Reader.Read<PlacementModifierBase>(TemplateProcessorStates(json));
        return (context, random, position, output) =>
        {
            var decoration = context.Decoration;
            var placement = new LegacyPlacementContext
            {
                Level = decoration.Level,
                Generation = new WorldGenerationContext(decoration.GeneratorMinY, decoration.GeneratorHeight),
                TopFeature = decoration.TopFeature,
                BiomeHasFeature = (biome, feature) => this.BiomeSteps(biome.Name)
                    .Any(step => step.Contains(feature.Identifier))
            };
            output.AddRange(modifier.GetPositions(placement, random, position));
        };
    }

    internal string[][] BiomeSteps(string biome)
    {
        if (this.FeatureSet is WorldgenFeatureSet set)
        {
            return set.BiomeFeatures.GetValueOrDefault(biome, [])
                .Select(step => step.Select(feature => feature.Identifier).ToArray()).ToArray();
        }

        return this.Data.Read("biome", biome).GetProperty("features").EnumerateArray()
            .Select(step => step.EnumerateArray().Select(feature =>
                WorldgenData.Qualify(feature.GetString()!)).ToArray()).ToArray();
    }

    internal IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<LegacyPlacedFeature>>> VanillaBiomeFeatures() =>
        this.Data.Ids("biome").ToDictionary(
            biome => biome,
            biome => (IReadOnlyList<IReadOnlyList<LegacyPlacedFeature>>)this.BiomeSteps(biome)
                .Select(step => (IReadOnlyList<LegacyPlacedFeature>)step.Select(this.PlacedAdapter).ToArray())
                .ToArray());

    internal bool Place(object entry, Func<bool> place) =>
        WorldgenFeatureSet.Place(this.OwnersOf(entry), "a 26.3 feature", place);

    internal bool HasOwners(object entry) => !this.OwnersOf(entry).IsEmpty;

    internal ImmutableArray<PluginContainer> OwnersOf(object entry)
    {
        if (!this.pluginLookups)
            return [];

        lock (this.compiling)
            return this.EntryOwners.GetValueOrDefault(entry, []);
    }

    private Feature Guard(Feature feature)
    {
        if (!this.dependencies.Of.ContainsKey(feature))
            return feature;

        var owners = this.OwnersOf(feature);
        Feature guarded = (context, random, origin) => WorldgenFeatureSet.Place(
            owners, "a 26.3 feature", () => feature(context, random, origin));
        this.CopyOwners(feature, guarded);
        if (this.pluginFeatures.TryGetValue(feature, out var implementation))
            this.pluginFeatures.Add(guarded, implementation);

        return guarded;
    }

    private static JsonElement FlattenFeature(JsonElement json)
    {
        if (!json.TryGetProperty("config", out var config))
            return json;

        var fields = JsonNode.Parse(config.GetRawText())!.AsObject();
        fields["type"] = json.GetProperty("type").GetString();
        return JsonSerializer.SerializeToElement(fields);
    }

    private sealed class RegistryFeature(FeatureRegistry registry, Feature feature) : ConfiguredFeatureBase
    {
        public override string Type => "obsidian:feature_registry";

        public override bool Place(FeatureContext context)
        {
            var decoration = new DecorationContext(
                registry,
                context.Level,
                position => context.Level.GetBiome(position).Name,
                context.Generation.MinY,
                context.Generation.Height)
            {
                TopFeature = context.TopFeature
            };
            return feature(decoration, context.Random, context.Origin);
        }
    }
}
