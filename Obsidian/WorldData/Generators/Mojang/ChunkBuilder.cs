using Obsidian.API.World.Generator.Noise;
using Obsidian.WorldData.Generators.Mojang.Carvers;
using Obsidian.WorldData.Generators.Mojang.Features;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Runs the vanilla overworld generation steps (biomes, noise, surface, carvers, features) for a level.
/// </summary>
internal sealed class ChunkBuilder
{
    private readonly NoiseSetting settings;
    private readonly TerrainGenerator terrainGenerator;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly MultiNoiseBiomeSource biomeSource;
    private readonly CarverStep carverStep;
    private readonly FeatureDecorator featureDecorator;

    public RandomState RandomState { get; }

    /// <param name="seed">World seed; see <see cref="RandomState.ParseSeed"/> for level seed strings.</param>
    public ChunkBuilder(long seed)
    {
        this.settings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];
        this.RandomState = new RandomState(this.settings, seed);

        this.terrainGenerator = new TerrainGenerator(this.RandomState);
        this.biomeSource = MultiNoiseBiomeSource.Overworld(this.RandomState);
        this.surfaceBuilder = new SurfaceBuilder(this.RandomState, this.biomeSource);
        this.carverStep = new CarverStep(this.RandomState, this.surfaceBuilder, this.biomeSource);
        this.featureDecorator = new FeatureDecorator(this.biomeSource.PossibleBiomes, BiomeFeatures.All);
    }

    /// <param name="chunk">Chunk to fill.</param>
    /// <param name="fluidUpdates">Receives fluid positions that need an update once the chunk is complete.</param>
    public void Generate3DTerrain(IChunk chunk, ICollection<Vector>? fluidUpdates = null) =>
        this.terrainGenerator.Generate(chunk, fluidUpdates);

    /// <summary>
    /// Stores the biome of every 4x4x4 cell of the chunk.
    /// </summary>
    public void PopulateBiomes(IChunk chunk)
    {
        var sampler = new NoiseChunk(this.RandomState, chunk.X, chunk.Z).ClimateSampler;
        var minQuartY = this.settings.Noise.MinY >> 2;
        var maxQuartY = minQuartY + (this.settings.Noise.Height >> 2);

        for (var quartX = 0; quartX < 4; quartX++)
        {
            for (var quartZ = 0; quartZ < 4; quartZ++)
            {
                for (var quartY = minQuartY; quartY < maxQuartY; quartY++)
                {
                    var biome = this.biomeSource.GetNoiseBiome(sampler, (chunk.X << 2) + quartX, quartY, (chunk.Z << 2) + quartZ);
                    chunk.SetBiome(quartX << 2, quartY << 2, quartZ << 2, biome);
                }
            }
        }
    }

    public void ApplySurfaceRules(IChunk chunk)
    {
        this.surfaceBuilder.BuildSurface(chunk);
        WorldgenHeightmaps.Update(chunk, this.settings.Noise.MinY, this.settings.Noise.Height);
    }

    /// <param name="chunk">Chunk to carve.</param>
    /// <param name="fluidUpdates">Receives fluid positions that need an update once the chunk is complete.</param>
    public void ApplyCarvers(IChunk chunk, ICollection<Vector>? fluidUpdates = null)
    {
        this.carverStep.Apply(chunk, fluidUpdates);
        WorldgenHeightmaps.Update(chunk, this.settings.Noise.MinY, this.settings.Noise.Height);
    }

    /// <summary>
    /// Places the biome features of chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>).
    /// </summary>
    /// <param name="area">The chunk and its 8 neighbors, all past the carvers step. Features may write into any of them.</param>
    /// <param name="scheduleFluidTick">Receives positions where features want a fluid update once generation completes.</param>
    /// <param name="onPlaced">Optional callback for each placed feature (step, global index, feature, placed anything).</param>
    public void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int chunkX, int chunkZ,
        Action<Vector>? scheduleFluidTick = null, Action<int, int, PlacedFeature, bool>? onPlaced = null)
    {
        var noise = this.settings.Noise;
        var region = new WorldGenRegion(area, chunkX, chunkZ, this.RandomState.Seed, noise.MinY, noise.Height, this.settings.SeaLevel,
            this.biomeSource, scheduleFluidTick);

        // Like vanilla, only the 3x3 chunks around the decorated chunk contribute biomes.
        var neighbors = area.Where(entry => Math.Abs(entry.Key.X - chunkX) <= 1 && Math.Abs(entry.Key.Z - chunkZ) <= 1)
            .Select(entry => entry.Value)
            .ToArray();

        this.featureDecorator.Decorate(region, neighbors, chunkX, chunkZ, onPlaced);
    }

    /// <summary>
    /// Writes the final heightmaps into the chunk; call it once every chunk around it is decorated.
    /// </summary>
    public void UpdateFinalHeightmaps(IChunk chunk) =>
        WorldgenHeightmaps.UpdateFinal(chunk, this.settings.Noise.MinY, this.settings.Noise.Height);
}
