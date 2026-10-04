using System.Text.Json.Nodes;
using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Writes <c>worldgen/</c>. Noises, noise settings and density functions are copied per file; configured features,
/// placed features, processor lists, structures, structure sets and template pools are grouped into one file per
/// category (<c>features/trees.json</c>), which WorldgenFeatureRegistryGenerator turns into nested registry classes
/// (<c>ConfiguredFeatures.Trees.Oak</c>).
/// </summary>
/// <remarks>
/// Features are grouped by the vanilla bootstrap class declaring them (TreeFeatures, VegetationPlacements, ...), which
/// the Java dumper lists since the data generators don't output it. The other registries are grouped by what
/// references them.
/// </remarks>
internal static class WorldgenAssets
{
    // The category (file name) of each vanilla class declaring configured or placed features.
    private static readonly Dictionary<string, string> featureClassCategories = new()
    {
        ["AquaticFeatures"] = "aquatic", ["AquaticPlacements"] = "aquatic",
        ["CaveFeatures"] = "caves", ["CavePlacements"] = "caves",
        ["EndFeatures"] = "end", ["EndPlacements"] = "end",
        ["MiscOverworldFeatures"] = "misc_overworld", ["MiscOverworldPlacements"] = "misc_overworld",
        ["NetherFeatures"] = "nether", ["NetherPlacements"] = "nether",
        ["OreFeatures"] = "ores", ["OrePlacements"] = "ores",
        ["PileFeatures"] = "piles",
        ["TreeFeatures"] = "trees", ["TreePlacements"] = "trees",
        ["VegetationFeatures"] = "vegetation", ["VegetationPlacements"] = "vegetation",
        ["VillagePlacements"] = "village",
    };

    // Entries referenced from several categories (or none) go to this category.
    private const string SharedCategory = "common";

    /// <param name="worldgenGroupsPath">
    /// The Java dumper's <c>{ "configured_feature": { id: class }, "placed_feature": { id: class } }</c>.
    /// </param>
    public static void Write(string dataDirectory, string reportsDirectory, string worldgenGroupsPath, string outputDirectory)
    {
        var worldgen = Path.Combine(dataDirectory, "worldgen");
        var output = Path.Combine(outputDirectory, "worldgen");

        foreach (var registry in new[] { "density_function", "noise", "noise_settings" })
            CopyDirectory(Path.Combine(worldgen, registry), Path.Combine(output, registry));

        var biomes = ReadEntries(Path.Combine(worldgen, "biome"));
        var biomeFeatures = new JsonObject();
        foreach (var (id, biome) in biomes)
            biomeFeatures[id] = biome["features"]!.DeepClone();

        WriteJson(Path.Combine(output, "biome_features.json"), biomeFeatures);

        // Template pools by structure, their first folder (village/plains/houses → village); the empty pool is shared.
        var templatePools = ReadEntries(Path.Combine(worldgen, "template_pool"));
        var poolCategories = templatePools.ToDictionary(pool => pool.Id, pool =>
        {
            var path = RemoveNamespace(pool.Id);
            var separator = path.IndexOf('/');
            return separator < 0 ? SharedCategory : path[..separator];
        });

        var featureClasses = ReadObject(worldgenGroupsPath);
        var configuredFeatures = ReadEntries(Path.Combine(worldgen, "configured_feature"));
        var placedFeatures = ReadEntries(Path.Combine(worldgen, "placed_feature"));

        // Processor lists go with what uses them: the template pools of a structure (their elements' processors) or
        // the configured features of a type (fossil processors).
        var featureUsers = configuredFeatures.Select(feature => (RemoveNamespace(feature.Json["type"]!.GetValue<string>()), feature.Json));
        var processorLists = ReadEntries(Path.Combine(worldgen, "processor_list"));
        var poolUsers = templatePools.Select(pool => (poolCategories[pool.Id], pool.Json));
        var processorListCategories = GetReferences(poolUsers.Concat(featureUsers),
            property => property.EndsWith("processors", StringComparison.Ordinal));

        // Structures by the dimension of their biomes, structure sets by the dimension of their structures.
        var biomeDimensions = GetBiomeDimensions(reportsDirectory);
        var biomeTags = Path.Combine(dataDirectory, "tags", "worldgen", "biome");
        var structures = ReadEntries(Path.Combine(worldgen, "structure"));
        var structureCategories = structures.ToDictionary(structure => structure.Id, structure =>
            biomeDimensions.GetValueOrDefault(ResolveBiomes(structure.Json["biomes"]!, biomeTags).First(), "end"));

        var structureSets = ReadEntries(Path.Combine(worldgen, "structure_set"));
        var structureSetCategories = structureSets.ToDictionary(set => set.Id,
            set => structureCategories[set.Json["structures"]![0]!["structure"]!.GetValue<string>()]);

        WriteCategories(Path.Combine(output, "features"), configuredFeatures,
            GetClassCategories(configuredFeatures, featureClasses["configured_feature"]!.AsObject(), worldgenGroupsPath));
        WriteCategories(Path.Combine(output, "placed_features"), placedFeatures,
            GetClassCategories(placedFeatures, featureClasses["placed_feature"]!.AsObject(), worldgenGroupsPath));
        WriteCategories(Path.Combine(output, "processor_lists"), processorLists,
            processorLists.ToDictionary(list => list.Id, list => processorListCategories.GetValueOrDefault(list.Id, SharedCategory)));
        WriteCategories(Path.Combine(output, "structures"), structures, structureCategories);
        WriteCategories(Path.Combine(output, "structure_sets"), structureSets, structureSetCategories);
        WriteCategories(Path.Combine(output, "template_pools"), templatePools, poolCategories);
    }

    private static List<(string Id, JsonNode Json)> ReadEntries(string directory) =>
        [.. ReadAll(directory).Select(entry => (DatagenAssets.ToId(entry.Path), entry.Json))];

    /// <summary>The category of each feature from the vanilla class declaring it.</summary>
    private static Dictionary<string, string> GetClassCategories(List<(string Id, JsonNode Json)> features, JsonObject classes,
        string worldgenGroupsPath) =>
        features.ToDictionary(feature => feature.Id, feature =>
        {
            var className = classes[feature.Id]?.GetValue<string>()
                ?? throw new InvalidDataException($"{worldgenGroupsPath} doesn't list the class declaring {feature.Id}.");

            return featureClassCategories.GetValueOrDefault(className)
                ?? throw new InvalidDataException($"{feature.Id} is declared by {className}, which has no category in {nameof(WorldgenAssets)}.");
        });

    /// <summary>
    /// The category of the entries the string properties matching <paramref name="isReference"/> of
    /// <paramref name="users"/> reference, from the categories of the users referencing them. Entries referenced by users
    /// of several categories are in the shared category.
    /// </summary>
    private static Dictionary<string, string> GetReferences(IEnumerable<(string Category, JsonNode Json)> users, Func<string, bool> isReference)
    {
        var categories = new Dictionary<string, string>();
        foreach (var (category, json) in users)
        {
            foreach (var (property, reference) in StringProperties(json))
            {
                if (isReference(property))
                    categories[reference] = categories.GetValueOrDefault(reference, category) == category ? category : SharedCategory;
            }
        }

        return categories;
    }

    // minecraft:tree → tree.
    private static string RemoveNamespace(string id) => id[(id.IndexOf(':') + 1)..];

    /// <summary>
    /// The dimension of each biome placed by a multi noise biome source preset (<c>overworld</c>, <c>nether</c>). The end's
    /// biome source places its biomes in code, so the biomes of neither preset are the end's.
    /// </summary>
    private static Dictionary<string, string> GetBiomeDimensions(string reportsDirectory)
    {
        var dimensions = new Dictionary<string, string>();
        foreach (var (preset, parameters) in ReadAll(Path.Combine(reportsDirectory, "biome_parameters", "minecraft")))
        {
            foreach (var entry in parameters["biomes"]!.AsArray())
                dimensions.TryAdd(entry!["biome"]!.GetValue<string>(), preset);
        }

        return dimensions;
    }

    /// <summary>The biomes of a biome holder set: a biome, a list of biomes or a <c>#tag</c>.</summary>
    private static IEnumerable<string> ResolveBiomes(JsonNode biomes, string tagsDirectory)
    {
        if (biomes is JsonArray array)
            return array.SelectMany(biome => ResolveBiomes(biome!, tagsDirectory));

        // Optional tag entries are { "id", "required" }.
        var id = biomes is JsonObject entry ? entry["id"]!.GetValue<string>() : biomes.GetValue<string>();
        if (!id.StartsWith('#'))
            return [id];

        var tag = Read(Path.Combine(tagsDirectory, RemoveNamespace(id) + ".json"));
        return ResolveBiomes(tag["values"]!, tagsDirectory);
    }

    /// <summary>Writes <c>category.json</c> files of the entries in each category, in the entries' order.</summary>
    private static void WriteCategories(string directory, List<(string Id, JsonNode Json)> entries, Dictionary<string, string> categories)
    {
        foreach (var category in entries.GroupBy(entry => categories[entry.Id]))
        {
            var combined = new JsonObject();
            foreach (var (id, json) in category)
                combined[id] = json;

            WriteJson(Path.Combine(directory, $"{category.Key}.json"), combined);
        }
    }
}
