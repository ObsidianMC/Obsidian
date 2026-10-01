using Obsidian.API.World.Generator.Noise;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Runs the vanilla overworld generation steps (biomes, noise, surface) for a level.
/// </summary>
internal sealed class ChunkBuilder
{
    private readonly NoiseSetting settings;
    private readonly TerrainGenerator terrainGenerator;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly MultiNoiseBiomeSource biomeSource;

    public RandomState RandomState { get; }

    /// <param name="seed">World seed; see <see cref="RandomState.ParseSeed"/> for level seed strings.</param>
    public ChunkBuilder(long seed)
    {
        this.settings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];
        this.RandomState = new RandomState(this.settings, seed);

        this.terrainGenerator = new TerrainGenerator(this.RandomState);
        this.biomeSource = MultiNoiseBiomeSource.Overworld(this.RandomState);
        this.surfaceBuilder = new SurfaceBuilder(this.RandomState, this.biomeSource);
    }

    public void Generate3DTerrain(IChunk chunk) => this.terrainGenerator.Generate(chunk);

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

    public void ApplySurfaceRules(IChunk chunk) => this.surfaceBuilder.BuildSurface(chunk);
}
