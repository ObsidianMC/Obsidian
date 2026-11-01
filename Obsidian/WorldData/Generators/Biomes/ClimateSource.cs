using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.Buffers;
using Obsidian.WorldData.Generators.Mojang.Regions;
using System.Threading;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// 26.3 biome selection over the float router, separate from the live legacy biome source: a dimension's
/// <see cref="BiomeLayout"/>, with plugins' biome regions over it (see <see cref="RegionLayout"/>).
/// </summary>
/// <remarks>
/// It's also the climate sampler plugins' biome sources read (<see cref="IClimateSampler"/>): the router's values, as
/// floats, before quantizing.
/// </remarks>
internal sealed class ClimateSource : IBiomeSource, IClimateSampler
{
    private static readonly string[] climateFields =
        ["temperature", "vegetation", "continents", "erosion", "depth", "ridges"];

    private const int ClimateCacheSize = 4096;

    // How many chunks' stored biomes are kept before all are forgotten at once: room for the neighbors of every chunk
    // generated around the same time. Filling a forgotten chunk again gives the same biomes (see Fill).
    private const int MaxStoredChunks = 4096;

    [ThreadStatic]
    private static CachedClimate[]? climateCache;

    private readonly DensityFunction[] functions;

    private readonly BiomeLayout layout;

    // The layout's parameter list when it's multi-noise, which filling searches itself.
    private readonly BiomeParameters? parameters;

    // Where plugins' biome regions are, or null without any.
    private readonly RegionLayout? regions;

    private readonly IReadOnlyList<string> possibleBiomes;

    // The parameter list's biomes by their name instances, which the tree's searches return: comparing references
    // is cheaper than hashing the names. Read only, so threads share it.
    private readonly Dictionary<string, BiomeCodec> codecs = new(ReferenceEqualityComparer.Instance);

    // The biomes this source returns, numbered for the stored chunks. Read only, so threads share them.
    private readonly List<BiomeCodec> palette = [];
    private readonly Dictionary<BiomeCodec, byte> paletteIndices = new(ReferenceEqualityComparer.Instance);

    private readonly ConcurrentDictionary<(int X, int Z), StoredBiomes> stored = new();
    private int storedCount;

    /// <param name="preset">minecraft:overworld, minecraft:nether, or minecraft:the_end.</param>
    public ClimateSource(WorldgenData data, DensityRouter router, string preset)
        : this(router, BiomeLayout.Vanilla(data, preset))
    {
    }

    /// <param name="layout">How the dimension's biome source picks biomes.</param>
    /// <param name="regions">Where plugins' biome regions are, or null for the layout alone.</param>
    public ClimateSource(DensityRouter router, BiomeLayout layout, RegionLayout? regions = null)
    {
        this.functions = climateFields.Select(field => router.Functions[field]).ToArray();
        this.layout = layout;
        this.parameters = (layout as MultiNoiseLayout)?.Parameters;
        this.regions = regions;
        this.possibleBiomes = PossibleNames(layout, regions);

        foreach (var (_, biome) in this.parameters?.Entries ?? [])
        {
            if (CodecRegistry.TryGetBiome(biome, out var codec))
                this.codecs.TryAdd(biome, codec!);
        }

        foreach (var biome in this.PossibleBiomes)
        {
            if (!CodecRegistry.TryGetBiome(biome, out var codec))
                continue;

            if (this.paletteIndices.TryAdd(codec!, checked((byte)this.palette.Count)))
                this.palette.Add(codec!);
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

    // Block lookups over uncached searches (carvers' top materials, BiomeTerrain.BiomeAt) mostly ask for quarts just
    // sampled. Each thread keeps the last climate samples by quart; the tree search still runs for each lookup.
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

    /// <summary>
    /// Source encounter order, which also determines decoration's feature identities: the layout's, followed by those
    /// its regions add.
    /// </summary>
    public IEnumerable<string> PossibleBiomes => this.possibleBiomes;

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
    {
        if (this.regions?.RegionAt(quartX, quartZ) is BiomeRegion region)
            return this.RegionBiome(region, quartX, quartY, quartZ);

        return this.LayoutBiome(quartX, quartY, quartZ);
    }

    // The layout's own biome, without regions.
    private BiomeCodec LayoutBiome(int quartX, int quartY, int quartZ)
    {
        if (this.parameters is not null)
            return this.Codec(this.parameters.Tree.Search(this.CachedSample(quartX, quartY, quartZ)));

        return this.layout.GetBiome(this, quartX, quartY, quartZ);
    }

    private BiomeCodec RegionBiome(BiomeRegion region, int quartX, int quartY, int quartZ)
    {
        if (region.ReadsClimate)
            return region.Select(null, this.CachedSample(quartX, quartY, quartZ));

        return region.Select(this.LayoutBiome(quartX, quartY, quartZ), default);
    }

    // The climate as plugins' biome sources read it: the router's float values, before quantizing.
    ClimatePoint IClimateSampler.Sample(int quartX, int quartY, int quartZ)
    {
        var x = quartX << 2;
        var y = quartY << 2;
        var z = quartZ << 2;

        return new ClimatePoint(
            this.functions[0].Sample(x, y, z),
            this.functions[1].Sample(x, y, z),
            this.functions[2].Sample(x, y, z),
            this.functions[3].Sample(x, y, z),
            this.functions[4].Sample(x, y, z),
            this.functions[5].Sample(x, y, z));
    }

    private static IReadOnlyList<string> PossibleNames(BiomeLayout layout, RegionLayout? regions)
    {
        if (regions is null)
            return layout.PossibleBiomes;

        var biomes = RegionLayout.PossibleBiomes(layout.PossibleCodecs(), regions.Regions);

        return [.. biomes.Select(biome => biome.Name)];
    }

    private BiomeCodec Codec(string name) => this.codecs.TryGetValue(name, out var codec) ? codec : GetBiome(name);

    // Spreads neighboring quarts over the cache's slots; cheaper than HashCode.Combine, which a lookup per block
    // would pay for.
    private static uint QuartHash(int quartX, int quartY, int quartZ) =>
        unchecked((uint)(quartX * 73856093 ^ quartY * 19349663 ^ quartZ * 83492791));

    /// <summary>Writes the BIOMES status before terrain: the biomes <see cref="Stored"/> gives the chunk.</summary>
    public void FillBiomes(IChunk chunk)
    {
        var biomes = this.Stored(chunk.X, chunk.Z, chunk.MinY, chunk.Height);

        for (var quartY = 0; quartY < biomes.QuartHeight; quartY++)
        {
            for (var x = 0; x < 4; x++)
            {
                for (var z = 0; z < 4; z++)
                {
                    chunk.SetBiome(x << 2, chunk.MinY + (quartY << 2), z << 2, biomes.Get(x, quartY, z));
                }
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.biomes);
    }

    /// <summary>
    /// The biomes the BIOMES status stores in a chunk with the given build range. Vanilla fills a chunk's biomes once
    /// and its neighbors' terrain reads them there, so they're kept for the neighbors, whether or not the chunk itself
    /// has been filled yet.
    /// </summary>
    public StoredBiomes Stored(int chunkX, int chunkZ, int minY, int height)
    {
        var key = (chunkX, chunkZ);

        if (this.stored.TryGetValue(key, out var biomes) && biomes.MinY == minY && biomes.Height == height)
            return biomes;

        biomes = this.Fill(chunkX, chunkZ, minY, height);

        if (Interlocked.Increment(ref this.storedCount) > MaxStoredChunks)
        {
            this.stored.Clear();
            Volatile.Write(ref this.storedCount, 0);
        }

        this.stored[key] = biomes;

        return biomes;
    }

    /// <summary>
    /// Vanilla's fillBiomesFromNoise: climate over a quart-spaced volume, searched by ascending section, then x, y, z
    /// within each section.
    /// </summary>
    /// <remarks>
    /// Each search starts from the result of the one before it, as vanilla's do on its thread, which decides exact
    /// ties. Vanilla's first search starts from whatever its thread searched last; here it starts from no result, as on
    /// a thread that hasn't searched yet. So a chunk's biomes don't depend on what was searched before them, and are
    /// the same however often and in whichever order chunks are filled.
    /// </remarks>
    private StoredBiomes Fill(int chunkX, int chunkZ, int minY, int height)
    {
        var volume = new DensityVolume(4, height >> 2, 4, chunkX << 4, minY, chunkZ << 4, 4, 4, 4);
        var indices = new byte[StoredBiomes.Size(height)];

        // Every chunk samples its climate once; renting the fields keeps that from allocating them each time.
        var climate = this.parameters is null
            ? null
            : ArrayPool<float>.Shared.Rent(this.functions.Length * volume.Size);

        try
        {
            if (climate is not null)
                this.SampleClimate(climate, volume);

            this.FillIndices(indices, climate, volume, chunkX, chunkZ, minY, height);
        }
        finally
        {
            if (climate is not null)
                ArrayPool<float>.Shared.Return(climate);
        }

        return new StoredBiomes(this.palette, indices, minY, height);
    }

    // Searches each quart's biome in vanilla's order (see Fill) into indices, from the sampled climate, or for the
    // end, from its own rule.
    private void FillIndices(
        byte[] indices,
        float[]? climate,
        in DensityVolume volume,
        int chunkX,
        int chunkZ,
        int minY,
        int height)
    {
        var run = new BiomeParameterTree<string>.SearchRun();
        var columnRegions = this.ColumnRegions(chunkX, chunkZ);

        for (var sectionY = 0; sectionY < height; sectionY += 16)
        {
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    var quartY = (sectionY >> 2) + y;

                    for (var z = 0; z < 4; z++)
                    {
                        BiomeCodec biome;

                        if (climate is null)
                        {
                            biome = this.GetNoiseBiome((chunkX << 2) + x, (minY >> 2) + quartY, (chunkZ << 2) + z);
                        }
                        else
                        {
                            var target = Target(climate, volume.Size, volume.Index(x, quartY, z));
                            biome = this.SearchFilled(target, columnRegions?[x * 4 + z], ref run);
                        }

                        indices[StoredBiomes.Index(x, quartY, z)] = this.paletteIndices[biome];
                    }
                }
            }
        }
    }

    // A multi-noise biome while filling: the region's where a region is, else the search continuing the chunk's run.
    private BiomeCodec SearchFilled(
        TargetPoint sample,
        BiomeRegion? region,
        ref BiomeParameterTree<string>.SearchRun run)
    {
        if (region is not null && region.ReadsClimate)
            return region.Select(null, sample);

        var biome = this.Codec(this.parameters!.Tree.Search(sample, ref run));

        return region is null ? biome : region.Select(biome, default);
    }

    // The region of each of a chunk's 16 quart columns, by x * 4 + z, or null without regions.
    private BiomeRegion?[]? ColumnRegions(int chunkX, int chunkZ)
    {
        if (this.regions is null)
            return null;

        var columns = new BiomeRegion?[16];

        for (var x = 0; x < 4; x++)
        {
            for (var z = 0; z < 4; z++)
            {
                columns[x * 4 + z] = this.regions.RegionAt((chunkX << 2) + x, (chunkZ << 2) + z);
            }
        }

        return columns;
    }

    // Samples each climate field over the volume into climate, one field after another (see Target).
    private void SampleClimate(float[] climate, in DensityVolume volume)
    {
        for (var field = 0; field < this.functions.Length; field++)
        {
            var values = climate.AsSpan(field * volume.Size, volume.Size);
            SampleField(this.functions[field], values, volume);
        }
    }

    // The quantized climate at a volume index of the fields SampleClimate sampled.
    private static TargetPoint Target(float[] climate, int size, int index) => new(
        Climate.Quantize(climate[index]),
        Climate.Quantize(climate[size + index]),
        Climate.Quantize(climate[2 * size + index]),
        Climate.Quantize(climate[3 * size + index]),
        Climate.Quantize(climate[4 * size + index]),
        Climate.Quantize(climate[5 * size + index]));

    // A flat field, such as the overworld's uncached temperature and vegetation noises, has the same value at every y
    // of a column at a point and over a volume (see DensityFunction.IsFlat), so one layer is sampled and copied up.
    private static void SampleField(DensityFunction function, Span<float> output, in DensityVolume volume)
    {
        if (!function.IsFlat || volume.SizeY == 1)
        {
            function.SampleVolume(output, volume);
            return;
        }

        var layer = volume with { SizeY = 1 };
        Span<float> layerValues = stackalloc float[layer.Size];
        function.SampleVolume(layerValues, layer);

        for (var indexZ = 0; indexZ < volume.SizeZ; indexZ++)
        {
            for (var indexX = 0; indexX < volume.SizeX; indexX++)
            {
                var value = layerValues[layer.Index(indexX, 0, indexZ)];
                output.Slice(volume.Index(indexX, 0, indexZ), volume.SizeY).Fill(value);
            }
        }
    }

    /// <summary>Vanilla's <c>the_end</c> biome source (see <see cref="EndLayout"/>).</summary>
    internal BiomeCodec EndBiome(int quartX, int quartY, int quartZ) =>
        this.Codec(this.EndBiomeName(quartX, quartY, quartZ));

    private string EndBiomeName(int quartX, int quartY, int quartZ)
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
