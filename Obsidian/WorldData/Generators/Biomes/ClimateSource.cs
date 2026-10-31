using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>26.3 biome selection over the float router, separate from the live legacy biome source.</summary>
internal sealed class ClimateSource : IBiomeSource
{
    private static readonly string[] climateFields =
        ["temperature", "vegetation", "continents", "erosion", "depth", "ridges"];

    private const int ClimateCacheSize = 4096;

    [ThreadStatic]
    private static CachedClimate[]? climateCache;

    private readonly DensityFunction[] functions;

    // Which of the functions have the same value at every y of a column (see DensityFunction.IsFlat).
    private readonly bool[] flat;
    private readonly BiomeParameters? parameters;

    // The parameter list's biomes by their name instances, which the tree's searches return: comparing references
    // is cheaper than hashing the names. Read only, so threads share it.
    private readonly Dictionary<string, BiomeCodec> codecs = new(ReferenceEqualityComparer.Instance);

    /// <param name="preset">minecraft:overworld, minecraft:nether, or minecraft:the_end.</param>
    public ClimateSource(WorldgenData data, DensityRouter router, string preset)
    {
        this.functions = climateFields.Select(field => router.Functions[field]).ToArray();
        this.flat = this.functions.Select(function => function.IsFlat).ToArray();
        this.parameters = preset == "minecraft:the_end" ? null : BiomeParameters.Read(data, preset);

        foreach (var (_, biome) in this.parameters?.Entries ?? [])
        {
            if (CodecRegistry.TryGetBiome(biome, out var codec))
                this.codecs.TryAdd(biome, codec!);
        }
    }

    public TargetPoint Sample(int quartX, int quartY, int quartZ)
    {
        var x = quartX << 2;
        var y = quartY << 2;
        var z = quartZ << 2;

        return new TargetPoint(
            Climate.Quantize(this.functions[0].Sample(x, y, z)),
            Climate.Quantize(this.functions[1].Sample(x, y, z)),
            Climate.Quantize(this.functions[2].Sample(x, y, z)),
            Climate.Quantize(this.functions[3].Sample(x, y, z)),
            Climate.Quantize(this.functions[4].Sample(x, y, z)),
            Climate.Quantize(this.functions[5].Sample(x, y, z)));
    }

    /// <summary>
    /// The climate fields of a quart column that are the same at every y, for <see cref="Sample(int, int, int, in
    /// TargetPoint)"/> to reuse for each quart of the column. The other fields are 0.
    /// </summary>
    public TargetPoint SampleColumn(int quartX, int quartZ)
    {
        var x = quartX << 2;
        var z = quartZ << 2;

        return new TargetPoint(
            this.ColumnField(0, x, z),
            this.ColumnField(1, x, z),
            this.ColumnField(2, x, z),
            this.ColumnField(3, x, z),
            this.ColumnField(4, x, z),
            this.ColumnField(5, x, z));
    }

    /// <summary>
    /// <see cref="Sample(int, int, int)"/>, taking the fields that are the same at every y from the quart's column
    /// (<see cref="SampleColumn"/>).
    /// </summary>
    public TargetPoint Sample(int quartX, int quartY, int quartZ, in TargetPoint column)
    {
        var x = quartX << 2;
        var y = quartY << 2;
        var z = quartZ << 2;

        return new TargetPoint(
            this.QuartField(0, column.Temperature, x, y, z),
            this.QuartField(1, column.Humidity, x, y, z),
            this.QuartField(2, column.Continentalness, x, y, z),
            this.QuartField(3, column.Erosion, x, y, z),
            this.QuartField(4, column.Depth, x, y, z),
            this.QuartField(5, column.Weirdness, x, y, z));
    }

    private long ColumnField(int field, int x, int z) =>
        this.flat[field] ? Climate.Quantize(this.functions[field].Sample(x, 0, z)) : 0L;

    private long QuartField(int field, long columnValue, int x, int y, int z) =>
        this.flat[field] ? columnValue : Climate.Quantize(this.functions[field].Sample(x, y, z));

    // Material rules look a biome up for each block, mostly in quarts just sampled. Each thread keeps the last
    // climate samples by quart; the tree search still runs for each lookup (see BiomeTerrain).
    private TargetPoint CachedSample(int quartX, int quartY, int quartZ)
    {
        var cache = climateCache ??= new CachedClimate[ClimateCacheSize];
        var slot = (int)(QuartHash(quartX, quartY, quartZ) & (ClimateCacheSize - 1));
        ref var entry = ref cache[slot];

        if (entry.Source == this && entry.X == quartX && entry.Y == quartY && entry.Z == quartZ)
        {
            return entry.Point;
        }

        var point = this.Sample(quartX, quartY, quartZ);
        entry = new CachedClimate(this, quartX, quartY, quartZ, point);

        return point;
    }

    /// <summary>The climate search over the parameter list, or <c>null</c> for the End's fixed regions.</summary>
    public BiomeParameterTree<string>? Tree => this.parameters?.Tree;

    /// <summary>Source encounter order, which also determines decoration's feature identities.</summary>
    public IEnumerable<string> PossibleBiomes => this.parameters is null
        ? ["minecraft:the_end", "minecraft:end_highlands", "minecraft:end_midlands",
            "minecraft:small_end_islands", "minecraft:end_barrens"]
        : this.parameters.Entries.Select(entry => entry.Biome).Distinct();

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
    {
        var name = this.parameters is null
            ? this.EndBiome(quartX, quartY, quartZ)
            : this.parameters.Tree.Search(this.CachedSample(quartX, quartY, quartZ));

        return this.codecs.TryGetValue(name, out var codec) ? codec : GetBiome(name);
    }

    // Spreads neighboring quarts over the cache's slots; cheaper than HashCode.Combine, which a lookup per block
    // would pay for.
    private static uint QuartHash(int quartX, int quartY, int quartZ) =>
        unchecked((uint)(quartX * 73856093 ^ quartY * 19349663 ^ quartZ * 83492791));

    /// <summary>
    /// Writes the BIOMES status before terrain. Climate uses a quart-spaced volume; search order is ascending
    /// section, then x, y, z within each section, matching vanilla's fillBiomesFromNoise tie history.
    /// </summary>
    public void FillBiomes(IChunk chunk)
    {
        var volume = new DensityVolume(4, chunk.Height >> 2, 4,
            chunk.X << 4, chunk.MinY, chunk.Z << 4, 4, 4, 4);
        var samples = this.parameters is null ? [] : this.SampleVolume(volume);

        for (var sectionY = 0; sectionY < chunk.Height; sectionY += 16)
        {
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    var quartY = (sectionY >> 2) + y;

                    for (var z = 0; z < 4; z++)
                    {
                        var index = volume.Index(x, quartY, z);
                        var biome = this.parameters is null
                            ? this.GetNoiseBiome((chunk.X << 2) + x, (chunk.MinY >> 2) + quartY,
                                (chunk.Z << 2) + z)
                            : GetBiome(this.parameters.Tree.Search(samples[index]));

                        chunk.SetBiome(x << 2, chunk.MinY + (quartY << 2), z << 2, biome);
                    }
                }
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.biomes);
    }

    private TargetPoint[] SampleVolume(DensityVolume volume)
    {
        var values = this.functions.Select(function =>
        {
            var buffer = new float[volume.Size];
            function.SampleVolume(buffer, volume);
            return buffer;
        }).ToArray();
        var targets = new TargetPoint[volume.Size];

        for (var index = 0; index < targets.Length; index++)
        {
            targets[index] = new TargetPoint(
                Climate.Quantize(values[0][index]),
                Climate.Quantize(values[1][index]),
                Climate.Quantize(values[2][index]),
                Climate.Quantize(values[3][index]),
                Climate.Quantize(values[4][index]),
                Climate.Quantize(values[5][index]));
        }

        return targets;
    }

    private string EndBiome(int quartX, int quartY, int quartZ)
    {
        var sectionX = quartX >> 2;
        var sectionZ = quartZ >> 2;

        if ((long)sectionX * sectionX + (long)sectionZ * sectionZ <= 4096L)
        {
            return "minecraft:the_end";
        }

        var erosion = this.functions[3].Sample(sectionX * 16 + 8, quartY << 2, sectionZ * 16 + 8);

        if (erosion > 0.25f)
        {
            return "minecraft:end_highlands";
        }

        if (erosion >= -0.0625f)
        {
            return "minecraft:end_midlands";
        }

        return erosion < -0.21875f ? "minecraft:small_end_islands" : "minecraft:end_barrens";
    }

    private readonly record struct CachedClimate(ClimateSource? Source, int X, int Y, int Z, TargetPoint Point);

    private static BiomeCodec GetBiome(string name) => CodecRegistry.TryGetBiome(name, out var biome)
        ? biome!
        : throw new InvalidOperationException($"Unknown biome '{name}'.");
}
