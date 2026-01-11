using Obsidian.API.Registries;
using Obsidian.API.World.Generator.Noise;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Orchestrates chunk generation by coordinating terrain, aquifer, surface, and biome systems.
/// Single Responsibility: High-level chunk generation workflow.
/// </summary>
internal class ChunkBuilder
{
    private readonly IWorld world;
    private readonly NoiseSetting settings;
    private readonly TerrainGenerator terrainGenerator;
    private readonly AquiferSystem aquiferSystem;
    private readonly SurfaceBuilder surfaceBuilder;
    private readonly IBiomeSource biomeSource;

    internal int Seed { get; private set; }

    public ChunkBuilder(IWorld world)
    {
        this.world = world;

        if (!int.TryParse(world.Seed, out int seedHash))
            seedHash = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(world.Seed)));

        Seed = seedHash;
        settings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];

        terrainGenerator = new TerrainGenerator(settings);
        aquiferSystem = new AquiferSystem(settings);
        biomeSource = new MultiNoiseBiomeSource(settings.NoiseRouter);
        surfaceBuilder = new SurfaceBuilder(settings, biomeSource);
    }

    public void Generate3DTerrain(IChunk chunk)
    {
        terrainGenerator.Generate(chunk, BlocksRegistry.GetFromSimpleState(settings.DefaultBlock), aquiferSystem);
    }

    public void PopulateBiomes(IChunk chunk)
    {
        // Biomes are stored in 4x4x4 sections
        for (int x = 0; x < 16; x += 4)
        {
            for (int z = 0; z < 16; z += 4)
            {
                int worldX = x + (chunk.X << 4);
                int worldZ = z + (chunk.Z << 4);

                for (int y = settings.Noise.MinY; y < settings.Noise.Height; y += 4)
                {
                    var biome = biomeSource.GetBiome(worldX, y, worldZ);
                    chunk.SetBiome(x, y, z, biome);
                }
            }
        }
    }

    public void ApplySurfaceRules(IChunk chunk)
    {
        surfaceBuilder.ApplySurfaceRules(chunk);
    }

    /// <summary>
    /// Updates the WorldSurfaceWG heightmap after terrain generation.
    /// This heightmap represents the world surface before features are added.
    /// Scans from top to bottom to find the highest non-air block.
    /// </summary>
    public void UpdateWorldSurfaceWGHeightmap(IChunk chunk)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                // Scan from top down to find the highest non-air block
                for (int y = settings.Noise.Height; y >= settings.Noise.MinY; y--)
                {
                    var block = chunk.GetBlock(x, y, z);
                    if (!block.IsAir)
                    {
                        chunk.Heightmaps[HeightmapType.WorldSurfaceWG].Set(x, z, y);
                        break;
                    }
                }
            }
        }
    }
}
