using Obsidian.API.World.Generator.RandomSources;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// A configured carver: a carver type with its settings, like vanilla's <c>ConfiguredWorldCarver</c>.
/// </summary>
internal interface IConfiguredCarver
{
    public bool IsStartChunk(IRandomSource random);

    public void Carve(CarvingContext context, IRandomSource random, int startChunkX, int startChunkZ);
}

/// <summary>
/// Reads configured carvers: vanilla's from <c>Assets/configured_carver</c>, and plugins' from their files, in
/// vanilla's format with the <c>minecraft:cave</c>, <c>minecraft:nether_cave</c> and <c>minecraft:canyon</c> types.
/// </summary>
internal static class ConfiguredCarvers
{
    private static readonly ConcurrentDictionary<string, IConfiguredCarver> vanilla = new();

    /// <summary>
    /// The configured carvers of vanilla's biomes, by biome identifier, as their files list them (Codecs/biomes.json).
    /// </summary>
    public static FrozenDictionary<string, ImmutableArray<IConfiguredCarver>> VanillaBiomes { get; } =
        LoadVanillaBiomes();

    /// <summary>
    /// One of vanilla's configured carvers, by identifier, like <c>minecraft:cave</c>; each is read once and shared.
    /// </summary>
    public static IConfiguredCarver Vanilla(string identifier) =>
        vanilla.GetOrAdd(identifier.Replace("minecraft:", string.Empty), LoadVanilla);

    /// <summary>
    /// Reads a plugin's configured carver file.
    /// </summary>
    /// <exception cref="InvalidDataException">The carver can't be read.</exception>
    public static IConfiguredCarver Read(JsonElement root)
    {
        try
        {
            return Parse(root);
        }
        catch (Exception ex) when (ex is KeyNotFoundException
            or InvalidOperationException
            or FormatException
            or NotSupportedException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static IConfiguredCarver LoadVanilla(string name)
    {
        var resource = $"Obsidian.Assets.configured_carver.{name}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing configured carver '{name}'.");
        using var document = JsonDocument.Parse(stream);

        return Parse(document.RootElement);
    }

    private static IConfiguredCarver Parse(JsonElement root)
    {
        var config = root.GetProperty("config");
        var type = root.GetProperty("type").GetString();

        var probability = config.GetProperty("probability").GetSingle();
        if (probability is < 0 or > 1)
            throw new InvalidDataException("config.probability: expected a number from 0 to 1");

        var y = HeightProvider.Parse(config.GetProperty("y"));
        var yScale = FloatProvider.Parse(config.GetProperty("yScale"));
        var lavaLevel = CarverAnchor.Parse(config.GetProperty("lava_level"));
        var replaceable = ResolveBlocks(config.GetProperty("replaceable"));

        CaveCarverConfiguration CaveConfiguration() => new()
        {
            Probability = probability,
            Y = y,
            YScale = yScale,
            LavaLevel = lavaLevel,
            Replaceable = replaceable,
            HorizontalRadiusMultiplier = FloatProvider.Parse(config.GetProperty("horizontal_radius_multiplier")),
            VerticalRadiusMultiplier = FloatProvider.Parse(config.GetProperty("vertical_radius_multiplier")),
            FloorLevel = FloatProvider.Parse(config.GetProperty("floor_level"))
        };

        CanyonCarverConfiguration CanyonConfiguration()
        {
            var shape = config.GetProperty("shape");

            return new CanyonCarverConfiguration
            {
                Probability = probability,
                Y = y,
                YScale = yScale,
                LavaLevel = lavaLevel,
                Replaceable = replaceable,
                VerticalRotation = FloatProvider.Parse(config.GetProperty("vertical_rotation")),
                DistanceFactor = FloatProvider.Parse(shape.GetProperty("distance_factor")),
                Thickness = FloatProvider.Parse(shape.GetProperty("thickness")),
                WidthSmoothness = shape.GetProperty("width_smoothness").GetInt32(),
                HorizontalRadiusFactor = FloatProvider.Parse(shape.GetProperty("horizontal_radius_factor")),
                VerticalRadiusDefaultFactor = shape.GetProperty("vertical_radius_default_factor").GetSingle(),
                VerticalRadiusCenterFactor = shape.GetProperty("vertical_radius_center_factor").GetSingle()
            };
        }

        return type switch
        {
            "minecraft:cave" => new ConfiguredCarver<CaveCarverConfiguration>(
                new CaveWorldCarver(),
                CaveConfiguration()),
            "minecraft:nether_cave" => new ConfiguredCarver<CaveCarverConfiguration>(
                new NetherWorldCarver(),
                CaveConfiguration()),
            "minecraft:canyon" => new ConfiguredCarver<CanyonCarverConfiguration>(
                new CanyonWorldCarver(),
                CanyonConfiguration()),
            _ => throw new NotSupportedException($"type: {type} isn't a carver type")
        };
    }

    // A block holder set's blocks as flags by block id: a #tag, which vanilla's carvers name, or a list of blocks, which
    // the legacy carvers have where the implemented version dropped their tag (see DatagenAssets.WriteLegacy).
    private static bool[] ResolveBlocks(JsonElement blocks)
    {
        int[] ids;
        if (blocks.ValueKind == JsonValueKind.Array)
        {
            ids = [.. blocks.EnumerateArray().Select(block => BlocksRegistry.TryGetDefault(block.GetString()!, out var match)
                ? match.RegistryId
                : throw new InvalidOperationException($"config.replaceable: {block} isn't a block"))];
        }
        else
        {
            var tag = blocks.GetString()!;
            var identifier = tag.TrimStart('#');
            if (!identifier.Contains(':'))
                identifier = $"minecraft:{identifier}";

            if (!tag.StartsWith('#') || !TagsRegistry.TryGet("block", identifier, out var match))
                throw new InvalidOperationException($"config.replaceable: {tag} isn't a #block tag");

            ids = [.. match.Entries];
        }

        var flags = new bool[ids.DefaultIfEmpty(-1).Max() + 1];
        foreach (var id in ids)
            flags[id] = true;

        return flags;
    }

    private static FrozenDictionary<string, ImmutableArray<IConfiguredCarver>> LoadVanillaBiomes()
    {
        var resource = "Obsidian.Assets.Codecs.biomes.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing biome data.");
        using var document = JsonDocument.Parse(stream);

        var biomes = new Dictionary<string, ImmutableArray<IConfiguredCarver>>();

        foreach (var entry in document.RootElement.GetProperty("value").EnumerateArray())
        {
            // Vanilla's holder set: one carver, or a list of them.
            var carvers = entry.GetProperty("element").GetProperty("carvers");
            var names = carvers.ValueKind == JsonValueKind.Array
                ? carvers.EnumerateArray().Select(carver => carver.GetString()!)
                : [carvers.GetString()!];

            biomes[entry.GetProperty("name").GetString()!] = [.. names.Select(Vanilla)];
        }

        return biomes.ToFrozenDictionary();
    }

    private sealed class ConfiguredCarver<TConfiguration>(
        WorldCarver<TConfiguration> carver,
        TConfiguration configuration) : IConfiguredCarver
        where TConfiguration : CarverConfiguration
    {
        public bool IsStartChunk(IRandomSource random) =>
            WorldCarver<TConfiguration>.IsStartChunk(configuration, random);

        public void Carve(CarvingContext context, IRandomSource random, int startChunkX, int startChunkZ) =>
            carver.Carve(context, configuration, random, startChunkX, startChunkZ);
    }
}
