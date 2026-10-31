using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.Threading;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>26.3 biome selection over the float router, separate from the live legacy biome source.</summary>
internal sealed class ClimateSource : IBiomeSource
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

    private readonly BiomeParameters? parameters;

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
    {
        this.functions = climateFields.Select(field => router.Functions[field]).ToArray();
        this.parameters = preset == "minecraft:the_end" ? null : BiomeParameters.Read(data, preset);

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

        return this.Codec(name);
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
        var samples = this.parameters is null ? [] : this.SampleVolume(volume);
        var indices = new byte[StoredBiomes.Size(height)];
        var run = new BiomeParameterTree<string>.SearchRun();

        for (var sectionY = 0; sectionY < height; sectionY += 16)
        {
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    var quartY = (sectionY >> 2) + y;

                    for (var z = 0; z < 4; z++)
                    {
                        var biome = this.parameters is null
                            ? this.GetNoiseBiome((chunkX << 2) + x, (minY >> 2) + quartY, (chunkZ << 2) + z)
                            : this.Codec(this.parameters.Tree.Search(samples[volume.Index(x, quartY, z)], ref run));

                        indices[StoredBiomes.Index(x, quartY, z)] = this.paletteIndices[biome];
                    }
                }
            }
        }

        return new StoredBiomes(this.palette, indices, minY, height);
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
