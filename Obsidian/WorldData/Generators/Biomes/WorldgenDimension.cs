using Obsidian.Plugins;
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
/// The 26.3 counterpart of <see cref="Mojang.MojangDimension"/>, with the same plugin lifecycle: the plugins whose
/// types it's made of are <see cref="Plugins"/>, which generating enters (a plugin unloading stops it), and
/// <see cref="Release"/> drops their objects so that they can unload. Its biome regions are fixed when its first
/// generator is created, like the 1.21.11 pipeline's (see <see cref="PluginBiomeRegions"/>).
/// </remarks>
internal sealed class WorldgenDimension
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
        IMaterialTypes? materialTypes = null)
    {
        this.Identifier = identifier;
        this.DimensionType = dimensionType;
        this.Data = data;
        this.SettingsId = settingsId;
        this.settings = settings;
        this.layout = layout;
        this.densityTypes = densityTypes;
        this.materialTypes = materialTypes;
        this.Plugins = plugins.IsDefault ? [] : plugins;
        this.regions = regions ?? (() => []);
        this.SourceBiomes = layout.PossibleBiomes;
    }

    /// <summary>The level stem's identifier, which names its level.</summary>
    public string Identifier { get; }

    /// <summary>The dimension type's identifier.</summary>
    public string DimensionType { get; }

    /// <summary>The 26.3 data the dimension is read from and generates with.</summary>
    public WorldgenData Data { get; }

    /// <summary>The noise settings' identifier, vanilla's or a plugin's, or null for ones defined inline.</summary>
    public string? SettingsId { get; }

    /// <summary>The plugins whose types the noise settings or the biome source are made of.</summary>
    public ImmutableArray<PluginContainer> Plugins { get; }

    /// <summary>
    /// The biome source's possible biomes, without those of biome regions; kept once the dimension is released.
    /// </summary>
    public IReadOnlyList<string> SourceBiomes { get; }

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
    public bool IsReleased => this.layout is null;

    /// <summary>
    /// The dimension's biome regions, fixed for the server's life as its first generator is created.
    /// </summary>
    public ImmutableArray<BiomeRegion> FixRegions() => this.regions();

    /// <summary>
    /// Drops the noise settings, the biome layout and the density and material types, so that the plugins they're
    /// made of can unload: the dimension can't create generators any more, and those it created stop as their
    /// plugins unload.
    /// </summary>
    public void Release()
    {
        this.settings = null;
        this.layout = null;
        this.densityTypes = null;
        this.materialTypes = null;
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
    /// <exception cref="InvalidDataException">The stem isn't valid, or the 26.3 pipeline can't generate it.</exception>
    public static WorldgenDimension Read(
        string identifier,
        byte[] stem,
        WorldgenNoise noise,
        WorldgenData data,
        Func<ImmutableArray<BiomeRegion>>? regions = null)
    {
        try
        {
            // The dimension type and biomes a stem defines inline are entries of their own, named for it.
            var (extracted, _) = LevelStemInline.Extract(identifier, stem);
            using var document = JsonDocument.Parse(extracted, documentOptions);

            return ReadStem(identifier, document.RootElement, noise, data, regions);
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
        Func<ImmutableArray<BiomeRegion>>? regions)
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

        var plugins = new HashSet<PluginContainer>();
        var layout = LevelStemData.ReadLayout(
            CodecJson.Required(generator, "generator", "biome_source"),
            "generator.biome_source",
            noise,
            data,
            plugins);

        var (densityTypes, materialTypes) = FindPluginTypes(data, settings, noise);
        plugins.UnionWith(densityTypes?.Owners ?? []);
        plugins.UnionWith(materialTypes?.Owners ?? []);

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
            materialTypes);
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

    private InvalidOperationException Released() =>
        new($"{this.Identifier} was released when a plugin it generates with unloaded.");
}
