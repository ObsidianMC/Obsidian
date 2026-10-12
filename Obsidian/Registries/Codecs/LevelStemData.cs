using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator;
using NoiseSetting = Obsidian.API.World.Generator.Noise.NoiseSetting;
using Obsidian.Plugins;
using Obsidian.WorldData.Generators.Mojang;
using System.IO;
using System.Text.Json;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads a level stem file (a data pack's <c>dimension/&lt;path&gt;.json</c>) like vanilla 1.21.11's LevelStem codec
/// reads it, into the dimension its generator builds.
/// </summary>
/// <remarks>
/// A stem is <c>{"type": dimension type, "generator": {...}}</c>. Only vanilla's <c>minecraft:noise</c> generator is
/// supported, with vanilla's or plugins' noise settings and any of vanilla's biome sources:
/// <c>minecraft:multi_noise</c> (an inline <c>biomes</c> list or a <c>preset</c>), <c>minecraft:fixed</c>,
/// <c>minecraft:checkerboard</c> and <c>minecraft:the_end</c>, or the biome source types plugins registered.
/// Noise settings and parameter list presets may be named or defined inline; dimension types and biomes are named.
/// Values are checked like vanilla's codecs check them, and references must exist; a stem vanilla would accept but
/// Obsidian can't generate is rejected with a message saying so.
/// </remarks>
internal static class LevelStemData
{
    private const string ExpectedParameter =
        "expected a number from -2 to 2, [min, max] or {\"min\": ..., \"max\": ...}";

    /// <summary>
    /// Reads a level stem.
    /// </summary>
    /// <param name="identifier">The stem's identifier, which names its dimension.</param>
    /// <param name="noise">The noise settings and worldgen types the stem can name.</param>
    /// <param name="entries">Gets the noise entries the stem reads: its noise settings, and the density functions
    /// and noises its biome source and inline noise settings name.</param>
    /// <exception cref="InvalidDataException">The stem isn't valid, or Obsidian can't generate it.</exception>
    public static MojangDimension Read(
        string identifier,
        JsonElement root,
        WorldgenNoise noise,
        ISet<object>? entries = null)
    {
        CodecJson.Object(root, "");

        var dimensionType = DimensionType(CodecJson.Required(root, "", "type"), "type");
        var generator = CodecJson.Object(CodecJson.Required(root, "", "generator"), "generator");
        var type = CodecJson.Identifier(CodecJson.Required(generator, "generator", "type"), "generator.type");

        return type switch
        {
            "minecraft:noise" => Noise(identifier, dimensionType, generator, noise, entries),
            "minecraft:flat" or "minecraft:debug" => throw CodecJson.Error(
                "generator.type",
                $"{type} generators aren't supported yet: use minecraft:noise"),
            _ => throw CodecJson.Error("generator.type", $"{type} isn't a generator type: expected minecraft:noise")
        };
    }

    private static MojangDimension Noise(
        string identifier,
        string dimensionType,
        JsonElement generator,
        WorldgenNoise noise,
        ISet<object>? entries)
    {
        const string settingsPath = "generator.settings";

        // The plugins whose code generating runs: those whose types the settings and the biome source are made of.
        var plugins = new HashSet<PluginContainer>();
        var (settingsId, settings) = NoiseSettings(CodecJson.Required(generator, "generator", "settings"));
        entries?.Add(settings);

        var (createSource, possibleBiomes) = BiomeSource(
            CodecJson.Required(generator, "generator", "biome_source"),
            "generator.biome_source",
            noise,
            plugins,
            entries);

        // Obsidian's terrain generator fills every cell of the noise settings, so they must lie in the build range;
        // vanilla would clamp them to it instead. Vanilla's own pairings all fit.
        var cells = settings.Noise;
        CodecRegistry.TryGetDimension(dimensionType, out var codec);
        var range = codec!.Element;

        if (cells.MinY < range.MinY || cells.MinY + cells.Height > range.MinY + range.Height)
        {
            throw CodecJson.Error(
                settingsPath,
                $"{settingsId ?? "the inline noise settings"} generate from y {cells.MinY} to "
                + $"{cells.MinY + cells.Height - 1}, outside the build range of {dimensionType} ({range.MinY} to "
                + $"{range.MinY + range.Height - 1})");
        }

        return new MojangDimension(
            identifier,
            dimensionType,
            settingsId,
            settings,
            createSource,
            possibleBiomes,
            [.. plugins]);

        // Noise settings by identifier, vanilla's or a plugin's, or defined inline like vanilla's registry file codec
        // allows: read like a plugin's noise_settings file, whose density functions may be named or inline too.
        (string? Identifier, NoiseSetting Settings) NoiseSettings(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
                return (null, noise.Reader(plugins, entries).Read<NoiseSetting>(value, settingsPath));

            var id = CodecJson.Identifier(value, settingsPath);
            if (noise.TryGetNoiseSettings(id, out var found))
            {
                plugins.UnionWith(noise.Owners(found));
                return (id, found);
            }

            var vanilla = string.Join(", ", NoiseRegistry.NoiseSettings.All.Keys.Order());

            throw CodecJson.Error(
                settingsPath,
                $"{id} isn't one of vanilla's noise settings ({vanilla}) or a loaded plugin's");
        }
    }

    // A dimension type by identifier: vanilla's or a plugin's.
    private static string DimensionType(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
            throw CodecJson.Error(path, "dimension types defined inline aren't supported: name a dimension_type file");

        var identifier = CodecJson.Identifier(value, path);

        return CodecRegistry.TryGetDimension(identifier, out _)
            ? identifier
            : throw CodecJson.Error(path, $"{identifier} isn't a dimension type");
    }

    /// <param name="plugins">Gets the plugins whose types a plugin's biome source is made of.</param>
    /// <param name="entries">Gets the density functions and noises a plugin's biome source names.</param>
    private static (Func<RandomState, IClimateBiomeSource> Create, Func<IReadOnlyList<BiomeCodec>> PossibleBiomes)
        BiomeSource(
            JsonElement value,
            string path,
            WorldgenNoise noise,
            HashSet<PluginContainer> plugins,
            ISet<object>? entries)
    {
        CodecJson.Object(value, path);
        var typePath = CodecJson.Field(path, "type");
        var type = CodecJson.Identifier(CodecJson.Required(value, path, "type"), typePath);

        switch (type)
        {
            case "minecraft:fixed":
            {
                var biome = Biome(CodecJson.Required(value, path, "biome"), CodecJson.Field(path, "biome"));
                var source = new FixedBiomeSource(biome);
                return (_ => source, () => source.PossibleBiomes);
            }
            case "minecraft:checkerboard":
            {
                var biomes = BiomeList(CodecJson.Required(value, path, "biomes"), CodecJson.Field(path, "biomes"));
                var source = new CheckerboardBiomeSource(biomes, Scale(value, CodecJson.Field(path, "scale")));
                return (_ => source, () => source.PossibleBiomes);
            }
            case "minecraft:multi_noise":
                return MultiNoise(value, path);
            case "minecraft:the_end":
                return (randomState => new TheEndBiomeSource(randomState), () => TheEndBiomeSource.Biomes);
            default:
                if (noise.Types.Find(type, typeof(BiomeSourceBase)) is null)
                {
                    throw CodecJson.Error(
                        typePath,
                        $"{type} isn't a biome source type: expected minecraft:multi_noise, minecraft:fixed, "
                        + "minecraft:checkerboard, minecraft:the_end or a type a plugin registered");
                }

                return PluginSource(value, path, noise, plugins, entries);
        }
    }

    // A biome source type a plugin registered, read like its other worldgen types. Its possible biomes are read
    // here, so that ordering features doesn't call the plugin. The plugins it's made of, counting those of the
    // density functions and noises it names, are added to the dimension's.
    private static (Func<RandomState, IClimateBiomeSource> Create, Func<IReadOnlyList<BiomeCodec>> PossibleBiomes)
        PluginSource(
            JsonElement value,
            string path,
            WorldgenNoise noise,
            HashSet<PluginContainer> plugins,
            ISet<object>? entries)
    {
        var source = noise.Reader(plugins, entries).Read<BiomeSourceBase>(value, path);
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

        return (randomState => new PluginBiomeSource(source, biomes, randomState), () => biomes);
    }

    // Like vanilla's Codec.mapEither: an inline biomes list, or else a preset.
    private static (Func<RandomState, IClimateBiomeSource> Create, Func<IReadOnlyList<BiomeCodec>> PossibleBiomes)
        MultiNoise(JsonElement value, string path)
    {
        InvalidDataException? listError = null;

        if (CodecJson.TryGet(value, "biomes", out var list))
        {
            try
            {
                var create = MultiNoiseBiomeSource.FromList(
                    ParameterList(list, CodecJson.Field(path, "biomes")),
                    out var possibleBiomes);

                return (create, () => possibleBiomes);
            }
            catch (InvalidDataException ex)
            {
                listError = ex;
            }
        }

        if (!CodecJson.TryGet(value, "preset", out var preset))
            throw listError ?? CodecJson.Error(path, "expected a biomes list or a preset");

        // A parameter list by identifier, or defined inline as vanilla's {"preset": ...}, which names a built-in one.
        var presetPath = CodecJson.Field(path, "preset");
        if (preset.ValueKind == JsonValueKind.Object)
        {
            preset = CodecJson.Required(preset, presetPath, "preset");
            presetPath = CodecJson.Field(presetPath, "preset");
        }

        return CodecJson.Identifier(preset, presetPath) switch
        {
            "minecraft:overworld" => (MultiNoiseBiomeSource.Overworld, () => MultiNoiseBiomeSource.OverworldBiomes),
            "minecraft:nether" => (MultiNoiseBiomeSource.Nether, () => MultiNoiseBiomeSource.NetherBiomes),
            var other => throw CodecJson.Error(
                presetPath,
                $"{other} isn't a parameter list preset: expected minecraft:overworld or minecraft:nether")
        };
    }

    // Vanilla's Climate.ParameterList codec: a non-empty list of {"parameters": point, "biome": biome}.
    private static List<(ParameterPoint Point, BiomeCodec Biome)> ParameterList(JsonElement value, string path)
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
    private static ClimateParameter Parameter(JsonElement value, string path)
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

    // A biome by identifier: vanilla's or a plugin's.
    private static BiomeCodec Biome(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
            throw CodecJson.Error(path, "biomes defined inline aren't supported: name a worldgen/biome file");

        var identifier = CodecJson.Identifier(value, path);

        return CodecRegistry.TryGetBiome(identifier, out var biome)
            ? biome!
            : throw CodecJson.Error(path, $"{identifier} isn't a biome");
    }

    // Vanilla's biome holder set: a biome, a list of them or a biome tag. Vanilla takes an empty one, but generating
    // with it fails, so it's rejected here.
    private static List<BiomeCodec> BiomeList(JsonElement value, string path)
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

    // Vanilla reads scale with intRange(0, 62).orElse(2): a missing or invalid scale is 2.
    private static int Scale(JsonElement source, string path)
    {
        if (!CodecJson.TryGet(source, "scale", out var value))
            return 2;

        try
        {
            return CodecJson.Int(value, path, 0, 62);
        }
        catch (InvalidDataException)
        {
            return 2;
        }
    }
}
