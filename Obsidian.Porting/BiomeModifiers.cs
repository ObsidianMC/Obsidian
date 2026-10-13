using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.Porting;

/// <summary>
/// Converts NeoForge's biome modifiers (<c>data/&lt;ns&gt;/neoforge/biome_modifier</c>) to Obsidian's
/// (<c>data/&lt;ns&gt;/biome_modifier</c>), one file per run of modifiers with the same biomes.
/// </summary>
/// <remarks>
/// NeoForge applies every modifier's additions before any removal, each phase in identifier order, as Obsidian does
/// with its modifiers' <c>add</c> and <c>remove</c>. So consecutive modifiers, in identifier order, with the same
/// biomes merge into one file without changing what's generated: features keep their order in each step.
/// </remarks>
public static class BiomeModifiers
{
    /// <summary>
    /// Where NeoForge's biome modifiers are, under <c>data/&lt;ns&gt;/</c>.
    /// </summary>
    public const string NeoForgeFolder = "neoforge/biome_modifier/";

    // Vanilla's decoration steps, in order, as both formats name them.
    internal static readonly string[] Steps =
    [
        "raw_generation",
        "lakes",
        "local_modifications",
        "underground_structures",
        "surface_structures",
        "strongholds",
        "underground_ores",
        "underground_decoration",
        "fluid_springs",
        "vegetal_decoration",
        "top_layer_modification"
    ];

    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions writeOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Converts a mod's NeoForge biome modifiers.
    /// </summary>
    /// <param name="files">
    /// The modifiers' files by path, <c>data/&lt;ns&gt;/neoforge/biome_modifier/...json</c>.
    /// </param>
    /// <param name="output">Gets Obsidian's modifier files by path.</param>
    /// <param name="report">Gets what was converted, merged or left out.</param>
    public static void Port(
        IEnumerable<KeyValuePair<string, byte[]>> files,
        IDictionary<string, byte[]> output,
        PortingReport report)
    {
        var converted = new List<(string Namespace, string Name, string Source, Modifier Modifier)>();

        // In identifier order, NeoForge's order.
        foreach (var (path, data) in files.OrderBy(file => Identifier(file.Key), StringComparer.Ordinal))
        {
            var (ns, name) = Split(path);

            try
            {
                var root = JsonNode.Parse(ModPorter.Utf8(data), documentOptions: documentOptions) as JsonObject
                    ?? throw new UnsupportedException("a biome modifier must be a JSON object");

                if (Convert(root) is Modifier modifier)
                    converted.Add((ns, name, path, modifier));
                else
                    report.Skip(path, "a neoforge:none modifier does nothing, so it was left out");
            }
            catch (Exception ex) when (ex is UnsupportedException or JsonException or InvalidOperationException)
            {
                report.Skip(path, ex.Message);
            }
        }

        // Runs of modifiers with the same biomes, merged into the first's file.
        var runs = new List<(string Path, Modifier Modifier, List<string> Sources)>();

        foreach (var (ns, name, source, modifier) in converted)
        {
            var path = $"data/{ns}/biome_modifier/{name}";

            if (runs.Count > 0
                && runs[^1].Path.StartsWith($"data/{ns}/", StringComparison.Ordinal)
                && runs[^1].Modifier.BiomeKey == modifier.BiomeKey)
            {
                runs[^1].Modifier.Merge(modifier);
                runs[^1].Sources.Add(source);
            }
            else
            {
                runs.Add((path, modifier, [source]));
            }
        }

        foreach (var (path, modifier, sources) in runs)
        {
            output[path] = JsonSerializer.SerializeToUtf8Bytes(modifier.ToJson(), writeOptions);

            var merged = sources.Count > 1 ? $" (merged {sources.Count} modifiers with the same biomes)" : "";
            report.Converted.Add($"{string.Join(", ", sources)} -> {path}{merged}");
        }
    }

    /// <summary>
    /// Converts one NeoForge biome modifier.
    /// </summary>
    /// <returns>Obsidian's modifier, or null for <c>neoforge:none</c>.</returns>
    /// <exception cref="UnsupportedException">It can't be converted; the message says why.</exception>
    internal static Modifier? Convert(JsonObject root)
    {
        if (root.ContainsKey("neoforge:conditions"))
            throw new UnsupportedException("it has NeoForge load conditions, which Obsidian doesn't read");

        var type = String(root, "type");

        if (type == "neoforge:none")
            return null;

        var biomes = HolderSet.Read(Required(root, "biomes"));
        if (biomes.All)
        {
            throw new UnsupportedException(
                "its biomes are every biome (less some), but Obsidian's modifiers list their biomes: name tags like "
                + "#minecraft:is_overworld instead");
        }

        var modifier = new Modifier(biomes.Entries, biomes.Excluded);

        switch (type)
        {
            case "neoforge:add_features":
                modifier.AddFeatures[Step(String(root, "step"))] = Set(root, "features", "features");
                break;

            case "neoforge:remove_features":
                var features = Set(root, "features", "features");

                foreach (var step in RemovedSteps(root))
                    modifier.RemoveFeatures[step] = features;

                break;

            case "neoforge:add_spawns":
                modifier.AddSpawners.AddRange(Spawners(Required(root, "spawners")));
                break;

            case "neoforge:remove_spawns":
                modifier.RemoveSpawners.AddRange(Set(root, "entity_types", "entity types"));
                break;

            // Carving steps are gone since 1.21.2, which is what Obsidian generates; a "step" is ignored.
            case "neoforge:add_carvers":
                modifier.AddCarvers.AddRange(Set(root, "carvers", "carvers"));
                break;

            case "neoforge:remove_carvers":
                modifier.RemoveCarvers.AddRange(Set(root, "carvers", "carvers"));
                break;

            case "neoforge:add_spawn_costs":
                var cost = Required(root, "spawn_cost");

                foreach (var entityType in Set(root, "entity_types", "entity types"))
                    modifier.AddSpawnCosts[EntityType(entityType)] = cost.DeepClone();

                break;

            case "neoforge:remove_spawn_costs":
                modifier.RemoveSpawnCosts.AddRange(Set(root, "entity_types", "entity types"));
                break;

            default:
                throw new UnsupportedException($"the modifier type {type} has no Obsidian equivalent");
        }

        return modifier;
    }

    // The steps remove_features removes from: "steps", one or a list, or every step.
    private static IEnumerable<string> RemovedSteps(JsonObject root) => root["steps"] switch
    {
        null => Steps,
        JsonArray steps => steps.Select(step => Step(step?.GetValue<string>())),
        JsonNode step => [Step(step.GetValue<string>())]
    };

    // Spawners, one or a list, in vanilla's form ({type, weight, minCount, maxCount}); a weighted entry's
    // {data, weight} form is flattened into it.
    private static IEnumerable<JsonNode> Spawners(JsonNode value)
    {
        var spawners = value is JsonArray list ? [.. list] : new List<JsonNode?> { value };

        foreach (var spawner in spawners)
        {
            if (spawner is not JsonObject entry)
                throw new UnsupportedException("a spawner must be an object");

            if (entry["data"] is not JsonObject data)
            {
                yield return entry;
                continue;
            }

            var flattened = (JsonObject)data.DeepClone();
            flattened["weight"] = entry["weight"]?.DeepClone();

            yield return flattened;
        }
    }

    // Obsidian keys spawn costs by entity type identifier, so a tag can't be given.
    private static string EntityType(JsonNode value) =>
        value is JsonValue identifier
            && identifier.TryGetValue<string>(out var name)
            && !name.StartsWith('#')
            ? name
            : throw new UnsupportedException("spawn costs can't be added to a tag of entity types in Obsidian");

    private static string Step(string? name) =>
        Steps.Contains(name)
            ? name!
            : throw new UnsupportedException($"'{name}' isn't a decoration step");

    private static List<JsonNode> Set(JsonObject root, string field, string what) =>
        HolderSet.Read(Required(root, field)).Listed(what);

    private static JsonNode Required(JsonObject root, string field) =>
        root[field] ?? throw new UnsupportedException($"it has no '{field}'");

    private static string String(JsonObject root, string field) =>
        Required(root, field) is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : throw new UnsupportedException($"its '{field}' isn't a string");

    // data/<ns>/neoforge/biome_modifier/<name>: the namespace and the name, with its extension.
    private static (string Namespace, string Name) Split(string path)
    {
        var parts = path.Split('/', 3);

        return (parts[1], parts[2][NeoForgeFolder.Length..]);
    }

    private static string Identifier(string path)
    {
        var (ns, name) = Split(path);

        return $"{ns}:{name}";
    }

    /// <summary>
    /// One of Obsidian's biome modifiers, as the converter builds it.
    /// </summary>
    internal sealed class Modifier(List<JsonNode> biomes, List<JsonNode> exclude)
    {
        public List<JsonNode> Biomes { get; } = biomes;

        public List<JsonNode> Exclude { get; } = exclude;

        public Dictionary<string, List<JsonNode>> AddFeatures { get; } = [];

        public List<JsonNode> AddSpawners { get; } = [];

        public List<JsonNode> AddCarvers { get; } = [];

        public Dictionary<string, JsonNode> AddSpawnCosts { get; } = [];

        public Dictionary<string, List<JsonNode>> RemoveFeatures { get; } = [];

        public List<JsonNode> RemoveSpawners { get; } = [];

        public List<JsonNode> RemoveCarvers { get; } = [];

        public List<JsonNode> RemoveSpawnCosts { get; } = [];

        /// <summary>
        /// The biomes it changes, the same for modifiers changing the same biomes however they list them.
        /// </summary>
        public string BiomeKey =>
            $"{string.Join(',', Sorted(this.Biomes))}|{string.Join(',', Sorted(this.Exclude))}";

        /// <summary>
        /// Takes in a later modifier with the same biomes: its additions go after this one's, as they would have
        /// been applied, and its removals join this one's.
        /// </summary>
        public void Merge(Modifier later)
        {
            foreach (var (step, features) in later.AddFeatures)
            {
                if (this.AddFeatures.TryGetValue(step, out var existing))
                    this.AddFeatures[step] = [.. existing, .. features];
                else
                    this.AddFeatures[step] = features;
            }

            this.AddSpawners.AddRange(later.AddSpawners);
            this.AddCarvers.AddRange(later.AddCarvers);

            // A later cost for a type replaces an earlier one, in both formats.
            foreach (var (type, cost) in later.AddSpawnCosts)
                this.AddSpawnCosts[type] = cost;

            foreach (var (step, features) in later.RemoveFeatures)
            {
                if (this.RemoveFeatures.TryGetValue(step, out var existing))
                    this.RemoveFeatures[step] = Distinct([.. existing, .. features]);
                else
                    this.RemoveFeatures[step] = features;
            }

            this.RemoveSpawners.AddRange(later.RemoveSpawners);
            this.RemoveCarvers.AddRange(later.RemoveCarvers);
            this.RemoveSpawnCosts.AddRange(later.RemoveSpawnCosts);
        }

        /// <summary>
        /// The modifier in Obsidian's format.
        /// </summary>
        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["biomes"] = Set(this.Biomes)
            };

            if (this.Exclude.Count > 0)
                json["exclude"] = Set(this.Exclude);

            var add = new JsonObject();

            if (this.AddFeatures.Count > 0)
                add["features"] = ByStep(this.AddFeatures);

            if (this.AddSpawners.Count > 0)
                add["spawners"] = Set(this.AddSpawners);

            if (this.AddCarvers.Count > 0)
                add["carvers"] = Set(this.AddCarvers);

            if (this.AddSpawnCosts.Count > 0)
            {
                add["spawn_costs"] = new JsonObject(this.AddSpawnCosts
                    .Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value.DeepClone())));
            }

            if (add.Count > 0)
                json["add"] = add;

            var remove = new JsonObject();

            if (this.RemoveFeatures.Count > 0)
                remove["features"] = this.RemovedFeatures();

            if (this.RemoveSpawners.Count > 0)
                remove["spawners"] = Set(Distinct(this.RemoveSpawners));

            if (this.RemoveCarvers.Count > 0)
                remove["carvers"] = Set(Distinct(this.RemoveCarvers));

            if (this.RemoveSpawnCosts.Count > 0)
                remove["spawn_costs"] = Set(Distinct(this.RemoveSpawnCosts));

            if (remove.Count > 0)
                json["remove"] = remove;

            return json;
        }

        // A plain set when the same features go from every step, as Obsidian reads it, or else by step.
        private JsonNode RemovedFeatures()
        {
            var first = this.RemoveFeatures.Values.First();
            var everyStep = this.RemoveFeatures.Count == Steps.Length
                && this.RemoveFeatures.Values.All(features => Key(features) == Key(first));

            return everyStep ? Set(first) : ByStep(this.RemoveFeatures);
        }

        private static JsonObject ByStep(Dictionary<string, List<JsonNode>> features) => new(Steps
            .Where(features.ContainsKey)
            .Select(step => KeyValuePair.Create(step, (JsonNode?)Set(features[step]))));

        // One entry as itself, more as a list.
        private static JsonNode Set(List<JsonNode> entries) =>
            entries.Count == 1
                ? entries[0].DeepClone()
                : new JsonArray([.. entries.Select(entry => entry.DeepClone())]);

        private static List<JsonNode> Distinct(List<JsonNode> entries) =>
            [.. entries.DistinctBy(entry => entry.ToJsonString())];

        private static string Key(List<JsonNode> entries) => string.Join(',', entries.Select(e => e.ToJsonString()));

        private static IEnumerable<string> Sorted(List<JsonNode> entries) =>
            entries.Select(entry => entry.ToJsonString()).Order(StringComparer.Ordinal);
    }
}
