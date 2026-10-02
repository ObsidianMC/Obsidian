using Obsidian.API.Registry.Codecs.Biomes;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Picks the biome whose climate ranges are closest to the sampled climate, like vanilla's multi-noise biome source.
/// </summary>
internal sealed class MultiNoiseBiomeSource : IClimateBiomeSource
{
    private static readonly Lazy<(BiomeParameterTree<BiomeCodec> Tree, IReadOnlyList<BiomeCodec> Biomes)> overworldParameters =
        new(() => LoadParameters("overworld"));

    private static readonly Lazy<(BiomeParameterTree<BiomeCodec> Tree, IReadOnlyList<BiomeCodec> Biomes)> netherParameters =
        new(() => LoadParameters("nether"));

    private readonly ClimateSampler climateSampler;
    private readonly BiomeParameterTree<BiomeCodec> parameters;

    /// <summary>
    /// Every biome this source can return, in the order they first appear in the parameter list.
    /// </summary>
    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; }

    private MultiNoiseBiomeSource(ClimateSampler climateSampler, BiomeParameterTree<BiomeCodec> parameters, IReadOnlyList<BiomeCodec> possibleBiomes)
    {
        this.climateSampler = climateSampler;
        this.parameters = parameters;
        this.PossibleBiomes = possibleBiomes;
    }

    /// <summary>
    /// Creates the source for vanilla's overworld biome layout (the <c>minecraft:overworld</c> parameter list preset).
    /// </summary>
    public static MultiNoiseBiomeSource Overworld(RandomState randomState) =>
        new(new ClimateSampler(randomState.Router), overworldParameters.Value.Tree, overworldParameters.Value.Biomes);

    /// <summary>
    /// Creates the source for vanilla's nether biome layout (the <c>minecraft:nether</c> parameter list preset).
    /// </summary>
    public static MultiNoiseBiomeSource Nether(RandomState randomState) =>
        new(new ClimateSampler(randomState.Router), netherParameters.Value.Tree, netherParameters.Value.Biomes);

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(this.climateSampler, quartX, quartY, quartZ);

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ) =>
        this.parameters.Search(sampler.Sample(quartX, quartY, quartZ));

    /// <summary>
    /// Loads a parameter list from <c>Assets/biome_parameters</c>.
    /// </summary>
    /// <remarks>
    /// The files are vanilla's <c>reports/biome_parameters/minecraft/*.json</c>, produced by
    /// <c>java -DbundlerMainClass=net.minecraft.data.Main -jar server.jar --reports</c>. Entry order matters (it decides ties).
    /// </remarks>
    private static (BiomeParameterTree<BiomeCodec> Tree, IReadOnlyList<BiomeCodec> Biomes) LoadParameters(string preset)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Obsidian.Assets.biome_parameters.{preset}.json")
            ?? throw new InvalidOperationException($"Missing biome parameter list '{preset}'.");
        using var document = JsonDocument.Parse(stream);

        var values = new List<(ParameterPoint, BiomeCodec)>();

        foreach (var entry in document.RootElement.GetProperty("biomes").EnumerateArray())
        {
            var name = entry.GetProperty("biome").GetString()!;
            if (!CodecRegistry.TryGetBiome(name, out var biome))
                throw new InvalidOperationException($"Unknown biome '{name}' in parameter list '{preset}'.");

            var parameters = entry.GetProperty("parameters");
            var point = new ParameterPoint(
                ReadParameter(parameters.GetProperty("temperature")),
                ReadParameter(parameters.GetProperty("humidity")),
                ReadParameter(parameters.GetProperty("continentalness")),
                ReadParameter(parameters.GetProperty("erosion")),
                ReadParameter(parameters.GetProperty("depth")),
                ReadParameter(parameters.GetProperty("weirdness")),
                Requantize(parameters.GetProperty("offset")));

            values.Add((point, biome!));
        }

        var possibleBiomes = values.Select(value => value.Item2).DistinctBy(biome => biome.Name).ToArray();
        return (new BiomeParameterTree<BiomeCodec>(values), possibleBiomes);
    }

    /// <summary>
    /// Reads a parameter written either as a single value or as <c>[min, max]</c>.
    /// </summary>
    private static ClimateParameter ReadParameter(JsonElement element) => element.ValueKind == JsonValueKind.Array
        ? new(Requantize(element[0]), Requantize(element[1]))
        : new(Requantize(element), Requantize(element));

    /// <summary>
    /// Reports store the quantized longs divided by 10000 as floats. Rounding recovers the exact long;
    /// re-quantizing through float would truncate values like 0.11 down by one.
    /// </summary>
    private static long Requantize(JsonElement element) => (long)Math.Round(element.GetDouble() * 10000.0);
}
