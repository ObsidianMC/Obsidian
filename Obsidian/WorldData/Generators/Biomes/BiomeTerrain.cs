using Obsidian.WorldData.Generators.Mojang;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Runs the new biome and terrain statuses together without switching the live generator.</summary>
internal sealed class BiomeTerrain
{
    private readonly TerrainGenerator terrain;
    private readonly BiomeManager manager;

    public BiomeTerrain(TerrainGenerator terrain, string preset)
    {
        this.terrain = terrain;
        this.Source = new ClimateSource(terrain.Data, terrain.Router, preset);
        this.manager = new BiomeManager(this.Source, terrain.Seed, terrain.Settings.MinY,
            terrain.Settings.Height, clampToHeight: false);
    }

    public ClimateSource Source { get; }

    public string BiomeAt(int x, int y, int z) => this.manager.GetBiome(x, y, z).Name;

    public IReadOnlyList<string> CarversAt(int chunkX, int chunkZ)
    {
        var biome = this.Source.GetNoiseBiome(chunkX << 2, 0, chunkZ << 2);
        var data = this.terrain.Data.Read("biome", biome.Name);

        return data.GetProperty("carvers").EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    public void Generate(IChunk chunk, ICollection<Vector>? fluidUpdates = null)
    {
        this.Source.FillBiomes(chunk);
        this.terrain.Generate(chunk, this.BiomeAt, this.CarversAt, fluidUpdates);
        // Chunk storage keeps the legacy enum until the live pipeline switches; carvers saves as 26.3 terrain.
        chunk.SetChunkStatus(ChunkGenStage.carvers);
    }
}
