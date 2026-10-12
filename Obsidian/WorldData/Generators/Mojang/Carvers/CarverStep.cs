using Obsidian.API.World.Generator.RandomSources;
using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// The carvers step of vanilla's NoiseBasedChunkGenerator: runs the carvers of every chunk within 8 chunks, each
/// start chunk with its biome's carvers.
/// </summary>
internal sealed class CarverStep
{
    private const int Radius = 8;

    // Start chunks' biomes, which vanilla keeps on each chunk; cleared once this many are kept.
    private const int MaxStartBiomes = 1 << 16;

    private readonly RandomState randomState;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly IClimateBiomeSource biomeSource;
    private readonly ConcurrentDictionary<long, string> startBiomes = new();

    // The carvers every possible biome shares for the biome carvers last given, if they share them.
    private CarverChoice? choice;

    // A chunk's carving buffers, kept by each thread for its next chunk. Taken while in use.
    [ThreadStatic]
    private static CarvingBuffers? freeBuffers;

    public CarverStep(RandomState randomState, SurfaceBuilder surfaceBuilder, IClimateBiomeSource biomeSource)
    {
        this.randomState = randomState;
        this.surfaceBuilder = surfaceBuilder;
        this.biomeSource = biomeSource;
    }

    /// <param name="biomeCarvers">
    /// Each biome's configured carvers, by biome identifier; a biome missing has none.
    /// </param>
    /// <param name="noiseChunk">
    /// The chunk's noise chunk when the other steps share it, or <c>null</c> for a new one.
    /// </param>
    public void Apply(
        IChunk chunk,
        IReadOnlyDictionary<string, ImmutableArray<IConfiguredCarver>> biomeCarvers,
        ICollection<Vector>? fluidUpdates = null,
        NoiseChunk? noiseChunk = null)
    {
        var settings = this.randomState.Settings;
        noiseChunk ??= new NoiseChunk(this.randomState, chunk.X, chunk.Z);

        var buffers = freeBuffers is not null
            && freeBuffers.MinY == settings.Noise.MinY
            && freeBuffers.Height == settings.Noise.Height
            ? freeBuffers
            : new CarvingBuffers(settings.Noise.MinY, settings.Noise.Height);
        freeBuffers = null;
        buffers.Mask.Clear();

        var context = new CarvingContext(this.surfaceBuilder, settings.Noise.MinY, settings.Noise.Height, buffers)
        {
            Chunk = chunk,
            NoiseChunk = noiseChunk,
            Aquifer = noiseChunk.Aquifer,
            Biomes = new BiomeManager(
                this.biomeSource,
                this.randomState.Seed,
                settings.Noise.MinY,
                settings.Noise.Height),
            FluidUpdates = fluidUpdates
        };

        // Carvers read the aquifer's last answer even where the lava level picks the block (see
        // WorldCarver.CarveBlock), so they start from a cleared one rather than the terrain fill's.
        context.Aquifer.ResetFluidUpdate();

        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        var uniform = this.Choose(biomeCarvers).Uniform;

        for (var offsetX = -Radius; offsetX <= Radius; offsetX++)
        {
            for (var offsetZ = -Radius; offsetZ <= Radius; offsetZ++)
            {
                var startChunkX = chunk.X + offsetX;
                var startChunkZ = chunk.Z + offsetZ;
                var carvers = uniform ?? biomeCarvers.GetValueOrDefault(this.StartBiome(startChunkX, startChunkZ), []);

                // Like vanilla, a carver's seed is its index in its start chunk's biome's list.
                for (var index = 0; index < carvers.Length; index++)
                {
                    random.SetLargeFeatureSeed(this.randomState.Seed + index, startChunkX, startChunkZ);

                    if (carvers[index].IsStartChunk(random))
                        carvers[index].Carve(context, random, startChunkX, startChunkZ);
                }
            }
        }

        freeBuffers = buffers;
    }

    // Vanilla's biome of a start chunk's carvers: the noise biome at its corner, at Y 0.
    private string StartBiome(int chunkX, int chunkZ)
    {
        var key = NumericsHelper.IntsToLong(chunkX, chunkZ);
        if (this.startBiomes.TryGetValue(key, out var biome))
            return biome;

        biome = this.biomeSource.GetNoiseBiome(chunkX << 2, 0, chunkZ << 2).Name;

        if (this.startBiomes.Count >= MaxStartBiomes)
            this.startBiomes.Clear();

        this.startBiomes[key] = biome;
        return biome;
    }

    // Vanilla's biomes of a dimension all list the same carvers, so its chunks are carved without looking start
    // chunks' biomes up, like before carvers came from biomes; only biomes with other carvers (plugins') need them.
    private CarverChoice Choose(IReadOnlyDictionary<string, ImmutableArray<IConfiguredCarver>> biomeCarvers)
    {
        var current = Volatile.Read(ref this.choice);
        if (current is not null && ReferenceEquals(current.Source, biomeCarvers))
            return current;

        var lists = this.biomeSource.PossibleBiomes
            .Select(biome => biomeCarvers.GetValueOrDefault(biome.Name, []))
            .ToList();

        ImmutableArray<IConfiguredCarver>? shared = null;
        if (lists.Count > 0 && lists.All(list => list.SequenceEqual(lists[0])))
            shared = lists[0];

        current = new CarverChoice(biomeCarvers, shared);

        Volatile.Write(ref this.choice, current);
        return current;
    }

    private sealed record CarverChoice(
        IReadOnlyDictionary<string, ImmutableArray<IConfiguredCarver>> Source,
        ImmutableArray<IConfiguredCarver>? Uniform);
}
