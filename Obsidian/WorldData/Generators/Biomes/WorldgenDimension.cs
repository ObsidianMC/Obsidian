using Obsidian.Plugins;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang.Regions;
using Obsidian.WorldData.Generators.Terrain;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// What a level stem generates with the 26.3 noise generator: its dimension type, noise settings and biome layout,
/// and the 26.3 data they're read from, which plugins' and data packs' entries may be laid over (see
/// <see cref="PluginWorldgen"/>). <see cref="BiomeTerrain.Create"/> generates it for a world.
/// </summary>
/// <remarks>
/// The plugins whose types or carver files it uses are <see cref="LevelStem.Plugins"/>, which generating enters.
/// <see cref="Release"/> drops their objects so that they can unload. Its biome regions are fixed when its first
/// generator is created (see <see cref="PluginBiomeRegions"/>).
/// </remarks>
internal sealed class WorldgenDimension : LevelStem
{
    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Func<ImmutableArray<BiomeRegion>> regions;

    // Dropped by Release, with the plugin objects they hold.
    private NoiseGeneratorSettings? settings;
    private BiomeLayout? layout;
    private IDensityTypes? densityTypes;
    private IMaterialTypes? materialTypes;

    /// <param name="identifier">The level stem's identifier.</param>
    /// <param name="dimensionType">The dimension type, which <paramref name="data"/> has.</param>
    /// <param name="data">The 26.3 data the settings name entries of.</param>
    /// <param name="settingsId">The noise settings' identifier, or null for ones defined inline.</param>
    /// <param name="settings">The noise settings.</param>
    /// <param name="layout">How the biome source picks biomes.</param>
    /// <param name="densityTypes">The density function types beyond vanilla's the settings may name.</param>
    /// <param name="plugins">The plugins whose types the settings or the biome source are made of.</param>
    /// <param name="regions">Fixes the dimension's biome regions as its first generator is created; none by
    /// default.</param>
    /// <param name="materialTypes">The material rule and condition types beyond vanilla's the settings may name.
    /// </param>
    /// <param name="carvers">The reloadable carver snapshot; null for standalone vanilla data.</param>
    public WorldgenDimension(
        string identifier,
        string dimensionType,
        WorldgenData data,
        string? settingsId,
        NoiseGeneratorSettings settings,
        BiomeLayout layout,
        IDensityTypes? densityTypes = null,
        ImmutableArray<PluginContainer> plugins = default,
        Func<ImmutableArray<BiomeRegion>>? regions = null,
        IMaterialTypes? materialTypes = null,
        Func<Terrain.Carvers.CarverRegistry>? carvers = null)
        : base(identifier, dimensionType, plugins)
    {
        this.DimensionTypeId = dimensionType;
        this.Data = data;
        this.SettingsId = settingsId;
        this.settings = settings;
        this.layout = layout;
        this.densityTypes = densityTypes;
        this.materialTypes = materialTypes;
        this.regions = regions ?? (() => []);
        this.SourceBiomeNames = layout.PossibleBiomes;
        this.SeaLevel = settings.SeaLevel;
        this.Carvers = carvers;
    }

    /// <summary>The dimension type's identifier.</summary>
    public string DimensionTypeId { get; }

    /// <summary>The 26.3 data the dimension is read from and generates with.</summary>
    public WorldgenData Data { get; }

    internal Func<Terrain.Carvers.CarverRegistry>? Carvers { get; private set; }

    /// <summary>The noise settings' identifier, vanilla's or a plugin's, or null for ones defined inline.</summary>
    public string? SettingsId { get; }

    /// <summary>
    /// The biome source's possible biomes, without those of biome regions; kept once the dimension is released.
    /// </summary>
    public IReadOnlyList<string> SourceBiomeNames { get; }

    public override IReadOnlyList<BiomeCodec> SourceBiomes =>
        this.SourceBiomeNames.Select(id => CodecRegistry.TryGetBiome(id, out var biome)
            ? biome! : throw new InvalidDataException($"Unknown biome {id}.")).ToArray();

    public int MinY => this.DimensionType.Element.MinY;

    public int Height => this.DimensionType.Element.Height;

    public int SeaLevel { get; }

    public bool HasSkyLight => this.DimensionType.Element.HasSkylight;

    public override IStemGenerator CreateGenerator() => new LevelStemGenerator(this);

    /// <summary>The noise settings.</summary>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    public NoiseGeneratorSettings Settings => this.settings ?? throw this.Released();

    /// <summary>How the biome source picks biomes.</summary>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    public BiomeLayout Layout => this.layout ?? throw this.Released();

    /// <summary>The density function types beyond vanilla's the settings may name, or null.</summary>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    public IDensityTypes? DensityTypes => this.IsReleased ? throw this.Released() : this.densityTypes;

    /// <summary>The material rule and condition types beyond vanilla's the settings may name, or null.</summary>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    public IMaterialTypes? MaterialTypes => this.IsReleased ? throw this.Released() : this.materialTypes;

    /// <summary>Whether <see cref="Release"/> dropped the generator's parts.</summary>
    public override bool IsReleased => this.layout is null;

    /// <summary>
    /// The dimension's biome regions, fixed for the server's life as its first generator is created.
    /// </summary>
    public ImmutableArray<BiomeRegion> FixRegions() => this.regions();

    /// <summary>
    /// Drops the noise settings, biome layout, density and material types, and carver provider so their
    /// plugins can unload: the dimension cannot create generators any more, and existing ones stop
    /// as their plugins unload.
    /// </summary>
    public override void Release()
    {
        this.settings = null;
        this.layout = null;
        this.densityTypes = null;
        this.materialTypes = null;
        this.Carvers = null;
    }

    /// <summary>
    /// One of vanilla's dimensions as vanilla's world preset defines it: <c>minecraft:overworld</c>,
    /// <c>minecraft:the_nether</c> or <c>minecraft:the_end</c>.
    /// </summary>
    /// <param name="data">The data it's read from; vanilla's by default.</param>
    public static WorldgenDimension Vanilla(string identifier, WorldgenData? data = null)
    {
        data ??= WorldgenData.Vanilla;

        var (settingsId, preset) = identifier switch
        {
            "minecraft:overworld" => ("minecraft:overworld", "minecraft:overworld"),
            "minecraft:the_nether" => ("minecraft:nether", "minecraft:nether"),
            "minecraft:the_end" => ("minecraft:end", "minecraft:the_end"),
            _ => throw new ArgumentException($"{identifier} isn't one of vanilla's dimensions.", nameof(identifier))
        };

        return new WorldgenDimension(
            identifier,
            identifier,
            data,
            settingsId,
            NoiseGeneratorSettings.Read(data, settingsId),
            BiomeLayout.Vanilla(data, preset));
    }

    /// <summary>
    /// Reads a level stem file whose generator is <c>minecraft:noise</c>, with 26.3's noise settings and biome
    /// sources. The noise settings, density functions and noises it names are <paramref name="data"/>'s; the biome
    /// source types and parameter lists, <paramref name="noise"/>'s.
    /// </summary>
    /// <remarks>
    /// Its terrain is built once for a placeholder seed, which checks the noise settings like generating would and
    /// finds the plugins whose types they're made of: the density function types of any of their functions (the
    /// router's, the aquifers', the material rules' and the spawn targets'), and the surface rule and condition types
    /// of their material rule. It keeps only those types (see <see cref="PluginDensityTypes.Narrowed"/> and
    /// <see cref="PluginMaterialTypes.Narrowed"/>).
    /// </remarks>
    /// <param name="identifier">The level stem's identifier.</param>
    /// <param name="stem">The stem file's bytes.</param>
    /// <param name="regions">Fixes the dimension's biome regions as its first generator is created; none by
    /// default.</param>
    /// <param name="carvers">The reloadable carver snapshot, also used to discover file-only owners.</param>
    /// <exception cref="InvalidDataException">The stem isn't valid, or the 26.3 pipeline can't generate it.</exception>
    public static WorldgenDimension Read(
        string identifier,
        byte[] stem,
        WorldgenNoise noise,
        WorldgenData data,
        Func<ImmutableArray<BiomeRegion>>? regions = null,
        Func<Terrain.Carvers.CarverRegistry>? carvers = null)
    {
        try
        {
            // The dimension type and biomes a stem defines inline are entries of their own, named for it.
            var (extracted, _) = LevelStemInline.Extract(identifier, stem);
            using var document = JsonDocument.Parse(extracted, documentOptions);

            return ReadStem(identifier, document.RootElement, noise, data, regions, carvers);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{identifier} isn't a valid level stem: {ex.Message}", ex);
        }
    }

    private static WorldgenDimension ReadStem(
        string identifier,
        JsonElement root,
        WorldgenNoise noise,
        WorldgenData data,
        Func<ImmutableArray<BiomeRegion>>? regions,
        Func<Terrain.Carvers.CarverRegistry>? carvers)
    {
        CodecJson.Object(root, "");

        var dimensionType = LevelStemData.DimensionType(CodecJson.Required(root, "", "type"), "type");
        var generator = CodecJson.Object(CodecJson.Required(root, "", "generator"), "generator");
        var type = CodecJson.Identifier(CodecJson.Required(generator, "generator", "type"), "generator.type");

        if (type != "minecraft:noise")
            throw CodecJson.Error("generator.type", $"the 26.3 pipeline only generates minecraft:noise, not {type}");

        var settingsValue = CodecJson.Required(generator, "generator", "settings");
        var settingsId = settingsValue.ValueKind == JsonValueKind.Object
            ? null
            : CodecJson.Identifier(settingsValue, "generator.settings");
        var settings = settingsId is null
            ? NoiseGeneratorSettings.Read(settingsValue.Clone(), "generator.settings")
            : NoiseGeneratorSettings.Read(data, settingsId);

        CodecRegistry.TryGetDimension(dimensionType, out var dimensionCodec);
        var range = dimensionCodec!.Element;
        if (settings.MinY < range.MinY || settings.MinY + settings.Height > range.MinY + range.Height)
            throw CodecJson.Error("generator.settings", "noise settings exceed the dimension build range");

        var plugins = new HashSet<PluginContainer>();
        var layout = LevelStemData.ReadLayout(
            CodecJson.Required(generator, "generator", "biome_source"),
            "generator.biome_source",
            noise,
            data,
            plugins,
            settings.LegacyRandomSource);

        var (densityTypes, materialTypes) = FindPluginTypes(data, settings, noise);
        plugins.UnionWith(densityTypes?.Owners ?? []);
        plugins.UnionWith(materialTypes?.Owners ?? []);

        if (carvers is not null)
        {
            var possible = PluginBiomeRegions.PossibleBiomes(identifier, layout.PossibleCodecs());
            plugins.UnionWith(carvers().OwnersOf(possible.Select(biome => biome.Name)));
        }

        return new WorldgenDimension(
            identifier,
            dimensionType,
            data,
            settingsId,
            settings,
            layout,
            densityTypes,
            [.. plugins],
            regions,
            materialTypes,
            carvers);
    }

    // Builds every density function and material rule the dimension's terrain generator builds, for a placeholder
    // seed, and gives the plugins' types they're made of, or null for none.
    private static (PluginDensityTypes? Density, PluginMaterialTypes? Materials) FindPluginTypes(
        WorldgenData data,
        NoiseGeneratorSettings settings,
        WorldgenNoise noise)
    {
        var density = new PluginDensityTypes(noise.Types);
        var materials = new PluginMaterialTypes(noise.Types);
        var terrain = new TerrainGenerator(data, 0L, settings, types: density, materialTypes: materials);

        foreach (var target in settings.SpawnTargets)
        {
            foreach (var function in target.Keys)
                terrain.Compiler.Function(function);
        }

        return (density.Narrowed(), materials.Narrowed());
    }

    public static WorldgenDimension Overworld { get; } = Vanilla("minecraft:overworld");

    public static WorldgenDimension Nether { get; } = Vanilla("minecraft:the_nether");

    public static WorldgenDimension End { get; } = Vanilla("minecraft:the_end");

    public static IReadOnlyList<WorldgenDimension> VanillaDimensions { get; } = [Overworld, Nether, End];

    private InvalidOperationException Released() =>
        new($"{this.Identifier} was released when a plugin it generates with unloaded.");
}
