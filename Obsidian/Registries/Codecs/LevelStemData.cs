using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;
using NoiseSetting = Obsidian.API.World.Generator.Noise.NoiseSetting;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using System.IO;
using System.Text.Json;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads a level stem file (a data pack's <c>dimension/&lt;path&gt;.json</c>) like vanilla 26.3's LevelStem codec
/// reads it, into the dimension its generator builds.
/// </summary>
/// <remarks>
/// <para>
/// A stem is <c>{"type": dimension type, "generator": {...}}</c>. <see cref="Read"/> takes the generators other than
/// <c>minecraft:noise</c>: <c>minecraft:flat</c>, with its layers and biome; <c>minecraft:debug</c>; or, as Obsidian's
/// extension, a loaded plugin's level generator (see <see cref="PluginGenerators"/>). A <c>minecraft:noise</c>
/// generator is read by the 26.3 pipeline (<see cref="WorldgenDimension.Read"/>), with its biome source read here
/// (see <see cref="ReadLayout"/>): <c>minecraft:multi_noise</c> (an inline <c>biomes</c> list or a <c>preset</c>),
/// <c>minecraft:fixed</c>, <c>minecraft:checkerboard</c>, <c>minecraft:the_end</c> or the biome source types plugins
/// registered.
/// </para>
/// <para>
/// Parameter lists may be named or defined inline. Dimension types and biomes are named here:
/// those a stem defines inline are entries of their own, named for the stem (see <see cref="LevelStemInline"/>).
/// Values are checked like vanilla's codecs check them, and references must exist; a stem vanilla would accept but
/// Obsidian can't generate is rejected with a message saying so.
/// </para>
/// </remarks>
internal static partial class LevelStemData
{
    private const string ExpectedParameter =
        "expected a number from -2 to 2, [min, max] or {\"min\": ..., \"max\": ...}";

    // Vanilla's DimensionType.Y_SIZE, the most a flat generator's layers may add up to.
    private const int MaxLayerHeight = 4064;

    /// <summary>
    /// Reads a level stem whose generator isn't <c>minecraft:noise</c>.
    /// </summary>
    /// <param name="identifier">The stem's identifier, which names its dimension.</param>
    /// <exception cref="InvalidDataException">The stem isn't valid, or Obsidian can't generate it.</exception>
    /// <exception cref="ArgumentException">The stem's generator is <c>minecraft:noise</c>, which
    /// <see cref="WorldgenDimension.Read"/> reads.</exception>
    public static LevelStem Read(string identifier, JsonElement root)
    {
        CodecJson.Object(root, "");

        var dimensionType = DimensionType(CodecJson.Required(root, "", "type"), "type");
        var generator = CodecJson.Object(CodecJson.Required(root, "", "generator"), "generator");
        var type = CodecJson.Identifier(CodecJson.Required(generator, "generator", "type"), "generator.type");

        return type switch
        {
            "minecraft:noise" => throw new ArgumentException(
                "minecraft:noise stems are read by WorldgenDimension.Read",
                nameof(root)),
            "minecraft:flat" => new FlatStem(identifier, dimensionType, Flat(generator)),
            "minecraft:debug" => new DebugStem(identifier, dimensionType),
            _ when PluginGenerators.Find(type) is not null =>
                new PluginGeneratorStem(identifier, dimensionType, type, generator.Clone()),
            _ => throw CodecJson.Error(
                "generator.type",
                $"{type} isn't a generator type: expected minecraft:noise, minecraft:flat, minecraft:debug or a "
                + "loaded plugin's level generator")
        };
    }

    // Vanilla's FlatLevelGeneratorSettings codec. Lakes, features and structures aren't generated in flat dimensions
    // yet, so settings asking for them are refused rather than generated without them.
    private static FlatLevelSettings Flat(JsonElement generator)
    {
        const string path = "generator.settings";
        var settings = CodecJson.Object(CodecJson.Required(generator, "generator", "settings"), path);

        var layersPath = CodecJson.Field(path, "layers");
        var layers = CodecJson.Array(CodecJson.Required(settings, path, "layers"), layersPath);
        var blocks = ImmutableArray.CreateBuilder<IBlock>();
        var index = 0;

        foreach (var layer in layers.EnumerateArray())
        {
            var layerPath = CodecJson.Index(layersPath, index++);
            CodecJson.Object(layer, layerPath);

            var heightPath = CodecJson.Field(layerPath, "height");
            var height = CodecJson.Int(CodecJson.Required(layer, layerPath, "height"), heightPath, 0, MaxLayerHeight);
            if (blocks.Count + height > MaxLayerHeight)
                throw CodecJson.Error(layersPath, $"the layers' heights add up to more than {MaxLayerHeight}");

            var block = LayerBlock(layer);
            for (var i = 0; i < height; i++)
                blocks.Add(block);
        }

        // Like vanilla's Codec.BOOL.fieldOf(...).orElse(false), only true asks for them.
        foreach (var flag in (ReadOnlySpan<string>)["lakes", "features"])
        {
            if (CodecJson.TryGet(settings, flag, out var value) && value.ValueKind == JsonValueKind.True)
            {
                throw CodecJson.Error(
                    CodecJson.Field(path, flag),
                    $"flat dimensions don't generate {flag} yet: use false");
            }
        }

        // Missing or invalid, vanilla places every structure set; only an empty list places none.
        var placesNoStructures = CodecJson.TryGet(settings, "structure_overrides", out var structures)
            && structures.ValueKind == JsonValueKind.Array
            && structures.GetArrayLength() == 0;

        if (!placesNoStructures)
        {
            throw CodecJson.Error(
                CodecJson.Field(path, "structure_overrides"),
                "flat dimensions don't generate structures yet: use [] (when it's missing, vanilla places every "
                + "structure set)");
        }

        return new FlatLevelSettings(blocks.ToImmutable(), FlatBiome(settings));
    }

    // A layer's block. Vanilla reads it with orElse(air), so a missing, invalid or unknown block is air.
    private static IBlock LayerBlock(JsonElement layer)
    {
        if (!CodecJson.TryGet(layer, "block", out var value)
            || value.ValueKind != JsonValueKind.String
            || !CodecJson.TryParseIdentifier(value.GetString()!, out var identifier))
        {
            return BlocksRegistry.Air;
        }

        try
        {
            return BlocksRegistry.Get(identifier);
        }
        catch (InvalidOperationException)
        {
            return BlocksRegistry.Air;
        }
    }

    // The flat generator's biome. Vanilla reads it leniently, so a missing or unknown biome is plains (which vanilla
    // logs).
    private static BiomeCodec FlatBiome(JsonElement settings)
    {
        if (CodecJson.TryGet(settings, "biome", out var value)
            && value.ValueKind == JsonValueKind.String
            && CodecJson.TryParseIdentifier(value.GetString()!, out var identifier)
            && CodecRegistry.TryGetBiome(identifier, out var biome))
        {
            return biome!;
        }

        return CodecRegistry.Biomes.Plains;
    }

    // A dimension type by identifier: vanilla's or a plugin's, or one the stem defined inline (see LevelStemInline).
    internal static string DimensionType(JsonElement value, string path)
    {
        var identifier = CodecJson.Identifier(value, path);

        return CodecRegistry.TryGetDimension(identifier, out _)
            ? identifier
            : throw CodecJson.Error(path, $"{identifier} isn't a dimension type");
    }

    // A plugin's biome source with its possible biomes, read here so that ordering features doesn't call the plugin.
    private static (BiomeSourceBase Source, List<BiomeCodec> Biomes) ReadPluginSource(
        JsonElement value,
        string path,
        WorldgenReader reader)
    {
        var source = reader.Read<BiomeSourceBase>(value, path);
        var biomesPath = CodecJson.Field(path, "possible_biomes");
        IReadOnlyList<string> names;

        try
        {
            names = source.PossibleBiomes;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw CodecJson.Error(biomesPath, ex.Message);
        }

        List<BiomeCodec> biomes = [];
        foreach (var name in names.Distinct())
        {
            if (!CodecRegistry.TryGetBiome(name, out var biome))
                throw CodecJson.Error(biomesPath, $"{source.Type} names {name}, which isn't a biome");

            biomes.Add(biome!);
        }

        if (biomes.Count == 0)
            throw CodecJson.Error(biomesPath, $"{source.Type} has no possible biomes");

        return (source, biomes);
    }

    // Vanilla's Climate.ParameterList codec: a non-empty list of {"parameters": point, "biome": biome}.
    internal static List<(ParameterPoint Point, BiomeCodec Biome)> ParameterList(JsonElement value, string path)
    {
        CodecJson.Array(value, path);

        if (value.GetArrayLength() == 0)
            throw CodecJson.Error(path, "can't be empty");

        var entries = new List<(ParameterPoint, BiomeCodec)>();
        var index = 0;

        foreach (var entry in value.EnumerateArray())
        {
            var entryPath = CodecJson.Index(path, index++);
            CodecJson.Object(entry, entryPath);

            var parametersPath = CodecJson.Field(entryPath, "parameters");
            var parameters = CodecJson.Object(CodecJson.Required(entry, entryPath, "parameters"), parametersPath);
            var biome = Biome(CodecJson.Required(entry, entryPath, "biome"), CodecJson.Field(entryPath, "biome"));

            entries.Add((Point(parameters, parametersPath), biome));
        }

        return entries;
    }

    private static ParameterPoint Point(JsonElement value, string path)
    {
        ClimateParameter Read(string name) =>
            Parameter(CodecJson.Required(value, path, name), CodecJson.Field(path, name));

        var offsetPath = CodecJson.Field(path, "offset");
        var offset = CodecJson.Float(CodecJson.Required(value, path, "offset"), offsetPath, 0f, 1f);

        return new ParameterPoint(
            Read("temperature"),
            Read("humidity"),
            Read("continentalness"),
            Read("erosion"),
            Read("depth"),
            Read("weirdness"),
            Climate.Quantize(offset));
    }

    // Vanilla's Climate.Parameter codec: a value from -2 to 2, or a range as [min, max] or {"min": ..., "max": ...},
    // quantized like vanilla since the parameters decide which biome generates where.
    internal static ClimateParameter Parameter(JsonElement value, string path)
    {
        float min;
        float max;

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                min = CodecJson.Float(value, path, -2f, 2f);
                max = min;
                break;
            case JsonValueKind.Array:
                if (value.GetArrayLength() != 2)
                    throw CodecJson.Error(path, $"expected [min, max], not a list of {value.GetArrayLength()}");

                min = CodecJson.Float(value[0], CodecJson.Index(path, 0), -2f, 2f);
                max = CodecJson.Float(value[1], CodecJson.Index(path, 1), -2f, 2f);
                break;
            case JsonValueKind.Object:
                min = CodecJson.Float(CodecJson.Required(value, path, "min"), CodecJson.Field(path, "min"), -2f, 2f);
                max = CodecJson.Float(CodecJson.Required(value, path, "max"), CodecJson.Field(path, "max"), -2f, 2f);
                break;
            default:
                throw CodecJson.Error(path, ExpectedParameter);
        }

        if (min > max)
            throw CodecJson.Error(path, $"min {min} is above max {max}");

        return new ClimateParameter(Climate.Quantize(min), Climate.Quantize(max));
    }

    // A biome by identifier: vanilla's or a plugin's, or one the stem defined inline (see LevelStemInline).
    internal static BiomeCodec Biome(JsonElement value, string path)
    {
        var identifier = CodecJson.Identifier(value, path);

        return CodecRegistry.TryGetBiome(identifier, out var biome)
            ? biome!
            : throw CodecJson.Error(path, $"{identifier} isn't a biome");
    }

    // Vanilla's biome holder set: a biome, a list of them or a biome tag. Vanilla takes an empty one, but generating
    // with it fails, so it's rejected here.
    internal static List<BiomeCodec> BiomeList(JsonElement value, string path)
    {
        List<BiomeCodec> biomes;

        if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith('#'))
        {
            var tag = CodecJson.Tag(value, path);
            if (!TagsRegistry.TryGet("worldgen/biome", tag, out var entries))
                throw CodecJson.Error(path, $"#{tag} isn't a biome tag");

            biomes = [.. entries.Entries.Select(id => CodecRegistry.TryGetBiome(id, out var biome) ? biome : null)
                .OfType<BiomeCodec>()];
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            biomes = [.. value.EnumerateArray().Select((biome, index) => Biome(biome, CodecJson.Index(path, index)))];
        }
        else
        {
            biomes = [Biome(value, path)];
        }

        return biomes.Count > 0 ? biomes : throw CodecJson.Error(path, "can't be empty");
    }

    /// <summary>
    /// Reads a checkerboard biome source's <c>scale</c> like vanilla, with <c>intRange(0, 62).orElse(2)</c>: a missing
    /// or invalid scale is 2.
    /// </summary>
    internal static int Scale(JsonElement? scale)
    {
        if (scale is not JsonElement { ValueKind: not JsonValueKind.Null } value)
            return 2;

        try
        {
            return CodecJson.Int(value, "scale", 0, 62);
        }
        catch (InvalidDataException)
        {
            return 2;
        }
    }
}
