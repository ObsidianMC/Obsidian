using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;
using NoiseSetting = Obsidian.API.World.Generator.Noise.NoiseSetting;
using Obsidian.Plugins;
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
/// A stem is <c>{"type": dimension type, "generator": {...}}</c>. The generator is one of vanilla's:
/// <c>minecraft:noise</c>, with vanilla's or plugins' noise settings and any of vanilla's biome sources
/// (<c>minecraft:multi_noise</c>, an inline <c>biomes</c> list or a <c>preset</c>, <c>minecraft:fixed</c>,
/// <c>minecraft:checkerboard</c> and <c>minecraft:the_end</c>) or the biome source types plugins registered;
/// <c>minecraft:flat</c>, with its layers and biome; or <c>minecraft:debug</c>. As Obsidian's extension, it may also
/// name a loaded plugin's level generator (see <see cref="PluginGenerators"/>).
/// </para>
/// <para>
/// Noise settings and parameter lists may be named or defined inline. Dimension types and biomes are named here:
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
    /// Reads a level stem.
    /// </summary>
    /// <param name="identifier">The stem's identifier, which names its dimension.</param>
    /// <param name="noise">The noise settings and worldgen types the stem can name.</param>
    /// <param name="entries">Gets the noise entries the stem reads: its noise settings, and the density functions
    /// and noises its biome source and inline noise settings name.</param>
    /// <exception cref="InvalidDataException">The stem isn't valid, or Obsidian can't generate it.</exception>
    public static LevelStem Read(
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

        // The noises the surface builder reads by name are read too, as the dimension keeps the files' ones.
        foreach (var name in SurfaceBuilder.NamedNoises)
        {
            if (noise.Noises.TryGetValue(name, out var named))
                entries?.Add(named);
        }

        var (createSource, possibleBiomes) = BiomeSource(
            CodecJson.Required(generator, "generator", "biome_source"),
            "generator.biome_source",
            noise,
            plugins,
            entries);

        CodecRegistry.TryGetDimension(dimensionType, out var codec);
        if (OutsideBuildRange(settingsId ?? "the inline noise settings", settings, codec!) is string outside)
            throw CodecJson.Error(settingsPath, outside);

        return new MojangDimension(
            identifier,
            dimensionType,
            settingsId,
            settings,
            createSource,
            possibleBiomes,
            [.. plugins],
            noise.Noises);

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

    /// <summary>
    /// Says how noise settings generate outside a dimension type's build range, or gives null when they fit.
    /// </summary>
    /// <remarks>
    /// Obsidian's terrain generator fills every cell of the noise settings, so they must lie in the build range;
    /// vanilla clamps generation to it instead. Like vanilla's, the lowest cell is the one holding <c>min_y</c>, so a
    /// <c>min_y</c> that isn't a multiple of the cell height (<c>size_vertical</c> * 4) fills from below it: with
    /// <c>size_vertical</c> 3, overworld settings fill from y -72. Vanilla's own pairings all fit.
    /// </remarks>
    /// <param name="name">The noise settings' name, for the message.</param>
    public static string? OutsideBuildRange(string name, NoiseSetting settings, DimensionCodec type)
    {
        var noise = settings.Noise;
        var range = type.Element;

        // The lowest filled block, as NoiseChunk.CellNoiseMinY rounds it; the highest is at most the noise's top.
        var cellHeight = noise.SizeVertical * 4;
        var lowest = Mth.FloorDiv(noise.MinY, cellHeight) * cellHeight;
        var top = noise.MinY + noise.Height;

        if (lowest >= range.MinY && top <= range.MinY + range.Height)
            return null;

        var rounded = lowest < noise.MinY
            ? $" (min_y {noise.MinY} rounded down to a whole cell of {cellHeight} blocks)"
            : "";

        return $"{name} generate from y {lowest}{rounded} to {top - 1}, outside the build range of "
            + $"{type.Name} ({range.MinY} to {range.MinY + range.Height - 1})";
    }

    // A dimension type by identifier: vanilla's or a plugin's, or one the stem defined inline (see LevelStemInline).
    internal static string DimensionType(JsonElement value, string path)
    {
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
                var scale = CodecJson.TryGet(value, "scale", out var scaleValue) ? scaleValue : (JsonElement?)null;
                var source = new CheckerboardBiomeSource(biomes, Scale(scale));
                return (_ => source, () => source.PossibleBiomes);
            }
            case "minecraft:multi_noise":
                return MultiNoise(value, path, noise, entries);
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
        var (source, biomes) = ReadPluginSource(value, path, noise, plugins, entries);

        return (randomState => new PluginBiomeSource(source, biomes, randomState), () => biomes);
    }

    // A plugin's biome source with its possible biomes, read here so that ordering features doesn't call the plugin.
    private static (BiomeSourceBase Source, List<BiomeCodec> Biomes) ReadPluginSource(
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

        return (source, biomes);
    }

    // Like vanilla's Codec.mapEither: an inline biomes list, or else a parameter list (a preset's).
    private static (Func<RandomState, IClimateBiomeSource> Create, Func<IReadOnlyList<BiomeCodec>> PossibleBiomes)
        MultiNoise(JsonElement value, string path, WorldgenNoise noise, ISet<object>? entries)
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

        // A parameter list by identifier, vanilla's or a file's, or defined inline as vanilla's {"preset": ...}; either
        // way it names a built-in preset.
        var presetPath = CodecJson.Field(path, "preset");
        ParameterListPreset parameters;

        if (preset.ValueKind == JsonValueKind.Object)
        {
            parameters = ParameterListPreset.Read(preset, presetPath);
        }
        else
        {
            var identifier = CodecJson.Identifier(preset, presetPath);
            if (!noise.TryGetParameterList(identifier, out var named))
                throw CodecJson.Error(presetPath, $"{identifier} isn't a multi-noise parameter list");

            parameters = named;
            entries?.Add(parameters);
        }

        return parameters.Preset == "minecraft:nether"
            ? (MultiNoiseBiomeSource.Nether, () => MultiNoiseBiomeSource.NetherBiomes)
            : (MultiNoiseBiomeSource.Overworld, () => MultiNoiseBiomeSource.OverworldBiomes);
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
