using System.Text.Json;
using static Obsidian.WorldData.Generators.Density.DensityJson;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>A range of a density function's value that the spawn may be found in.</summary>
internal readonly record struct SpawnTargetRange(float Min, float Max);

/// <summary>
/// A 26.3 noise settings entry (<c>worldgen/noise_settings</c>), with its density functions still as JSON:
/// <see cref="DensityRouter.Create"/> builds them for a world.
/// </summary>
/// <param name="Router">The <c>noise_router</c> object: its eight functions by field name.</param>
/// <param name="Aquifers">
/// The optional <c>aquifers</c> object (barrier, fluid_level_floodedness, fluid_level_spread, lava, exclusion,
/// surface_level), or null where the dimension has none. 26.3 moved these out of the router.
/// </param>
/// <param name="SpawnTargets">
/// Each spawn target as a map from a density function's identifier to the range its value must be in.
/// </param>
/// <param name="MaterialRule">The <c>material_rule</c> (a reference or a rule), read by the terrain step.</param>
internal sealed record NoiseGeneratorSettings(
    int MinY,
    int Height,
    int SeaLevel,
    string DefaultBlock,
    string DefaultFluid,
    bool DisableMobGeneration,
    bool LegacyRandomSource,
    JsonElement Router,
    JsonElement? Aquifers,
    ImmutableArray<ImmutableDictionary<string, SpawnTargetRange>> SpawnTargets,
    JsonElement MaterialRule)
{
    /// <summary>The router's fields, which vanilla's NoiseRouter codec requires.</summary>
    public static readonly ImmutableArray<string> RouterFields =
    [
        "temperature",
        "vegetation",
        "continents",
        "erosion",
        "depth",
        "ridges",
        "chunk_surface_level",
        "final_density"
    ];

    /// <summary>The fields of <c>aquifers</c>, which vanilla's codec requires when it's present.</summary>
    public static readonly ImmutableArray<string> AquiferFields =
    [
        "barrier",
        "fluid_level_floodedness",
        "fluid_level_spread",
        "lava",
        "exclusion",
        "surface_level"
    ];

    /// <summary>Reads a vanilla noise settings entry, such as <c>minecraft:overworld</c>.</summary>
    public static NoiseGeneratorSettings Read(WorldgenData data, string id) =>
        Read(data.Read("noise_settings", id), id);

    /// <param name="path">Where the value is, for messages.</param>
    public static NoiseGeneratorSettings Read(JsonElement json, string path)
    {
        var noise = Required(json, path, "noise");
        var noisePath = Field(path, "noise");
        var minY = Int(Required(noise, noisePath, "min_y"), Field(noisePath, "min_y"), -2032, 2031);
        var height = Int(Required(noise, noisePath, "height"), Field(noisePath, "height"), 0, 4064);

        if (minY % 16 != 0 || height % 16 != 0)
            throw Error(noisePath, "min_y and height have to be multiples of 16");

        if (minY + height > 2032)
            throw Error(noisePath, "min_y + height cannot be higher than 2032");

        var router = Required(json, path, "noise_router");
        foreach (var field in RouterFields)
            Required(router, Field(path, "noise_router"), field);

        JsonElement? aquifers = json.TryGetProperty("aquifers", out var aquifersJson) ? aquifersJson : null;
        if (aquifers is { } presentAquifers)
        {
            foreach (var field in AquiferFields)
                Required(presentAquifers, Field(path, "aquifers"), field);
        }

        return new NoiseGeneratorSettings(
            minY,
            height,
            Int(Required(json, path, "sea_level"), Field(path, "sea_level")),
            BlockName(Required(json, path, "default_block"), Field(path, "default_block")),
            BlockName(Required(json, path, "default_fluid"), Field(path, "default_fluid")),
            Boolean(Required(json, path, "disable_mob_generation"), Field(path, "disable_mob_generation")),
            Boolean(Required(json, path, "legacy_random_source"), Field(path, "legacy_random_source")),
            router,
            aquifers,
            ReadSpawnTargets(Required(json, path, "spawn_target"), Field(path, "spawn_target")),
            Required(json, path, "material_rule"));
    }

    // A block state's name: 26.3 writes a block without properties as its identifier, older data as {"Name": ...}.
    private static string BlockName(JsonElement json, string path)
    {
        if (json.ValueKind == JsonValueKind.String)
            return json.GetString()!;

        return String(Required(json, path, "Name"), Field(path, "Name"));
    }

    private static ImmutableArray<ImmutableDictionary<string, SpawnTargetRange>> ReadSpawnTargets(JsonElement json,
        string path)
    {
        var targets = ImmutableArray.CreateBuilder<ImmutableDictionary<string, SpawnTargetRange>>();
        var index = 0;

        foreach (var target in Array(json, path))
        {
            var targetPath = Index(path, index);
            var ranges = ImmutableDictionary.CreateBuilder<string, SpawnTargetRange>();

            foreach (var property in Object(target, targetPath).EnumerateObject())
                ranges[property.Name] = ReadRange(property.Value, Field(targetPath, property.Name));

            targets.Add(ranges.ToImmutable());
            index++;
        }

        return targets.ToImmutable();
    }

    // A number, [min, max] or {"min": ..., "max": ...}.
    private static SpawnTargetRange ReadRange(JsonElement json, string path)
    {
        SpawnTargetRange range;

        switch (json.ValueKind)
        {
            case JsonValueKind.Number:
                var value = Float(json, path);
                range = new SpawnTargetRange(value, value);
                break;
            case JsonValueKind.Array when json.GetArrayLength() == 2:
                range = new SpawnTargetRange(Float(json[0], Index(path, 0)), Float(json[1], Index(path, 1)));
                break;
            case JsonValueKind.Object:
                var min = Float(Required(json, path, "min"), Field(path, "min"));
                var max = Float(Required(json, path, "max"), Field(path, "max"));
                range = new SpawnTargetRange(min, max);
                break;
            default:
                throw Error(path, "expected a number, [min, max] or {\"min\": ..., \"max\": ...}");
        }

        return range.Min <= range.Max ? range : throw Error(path, $"min {range.Min} is above max {range.Max}");
    }
}

/// <summary>
/// A noise settings entry's density functions, built for one world: the eight router functions and, where the
/// dimension has aquifers, the aquifer functions.
/// </summary>
internal sealed class DensityRouter
{
    private DensityRouter(ImmutableDictionary<string, DensityFunction> functions,
        ImmutableDictionary<string, DensityFunction>? aquifers)
    {
        this.Functions = functions;
        this.Aquifers = aquifers;
    }

    /// <summary>The router functions by their field name (<see cref="NoiseGeneratorSettings.RouterFields"/>).</summary>
    public ImmutableDictionary<string, DensityFunction> Functions { get; }

    /// <summary>The aquifer functions by field name, or null without aquifers.</summary>
    public ImmutableDictionary<string, DensityFunction>? Aquifers { get; }

    public DensityFunction Continents => this.Functions["continents"];

    public DensityFunction FinalDensity => this.Functions["final_density"];

    public static DensityRouter Create(NoiseGeneratorSettings settings, DensityCompiler compiler)
    {
        var functions = Compile(settings.Router, NoiseGeneratorSettings.RouterFields, "noise_router", compiler);
        var aquifers = settings.Aquifers is { } aquiferJson
            ? Compile(aquiferJson, NoiseGeneratorSettings.AquiferFields, "aquifers", compiler)
            : null;

        return new DensityRouter(functions, aquifers);
    }

    private static ImmutableDictionary<string, DensityFunction> Compile(JsonElement json,
        ImmutableArray<string> fields, string path, DensityCompiler compiler)
    {
        var functions = ImmutableDictionary.CreateBuilder<string, DensityFunction>();

        foreach (var field in fields)
            functions[field] = compiler.Compile(json.GetProperty(field), Field(path, field));

        return functions.ToImmutable();
    }
}
