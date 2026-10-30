using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang.Features;
using System.Text.Json;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Runs the 26.3 biome, terrain and features statuses without switching the live generator.</summary>
internal sealed class BiomeTerrain
{
    private readonly TerrainGenerator terrain;
    private readonly BiomeManager manager;
    private readonly FeatureRegistry features;
    private readonly int unlitBrightness;
    private BiomeDecoration? decoration;

    public BiomeTerrain(TerrainGenerator terrain, string preset, FeatureRegistry? features = null)
    {
        this.terrain = terrain;
        this.features = features ?? new FeatureRegistry(terrain.Data);
        var dimension = preset == "minecraft:nether" ? "minecraft:the_nether" : preset;
        var hasSkylight = terrain.Data.Read("dimension_type", dimension).GetProperty("has_skylight").GetBoolean();
        this.unlitBrightness = hasSkylight ? 15 : 0;
        this.Source = new ClimateSource(terrain.Data, terrain.Router, preset);
        // Point lookups must revisit the tree: memoizing a quart changes later exact ties' previous-leaf input.
        this.manager = new BiomeManager(this.Source, terrain.Seed, terrain.Settings.MinY,
            terrain.Settings.Height, cacheNoiseBiomes: false, clampToHeight: false);
    }

    public ClimateSource Source { get; }

    public BiomeDecoration Decoration => this.decoration ??= new(this.features, this.Source.PossibleBiomes);

    /// <summary>
    /// Decorates one terrain-status chunk. Its eight terrain-status neighbors must already exist, and all writes
    /// are retained for later decoration. The live generator does not call this 26.3 pipeline yet.
    /// </summary>
    public WorldGenRegion Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int chunkX, int chunkZ)
    {
        var center = chunks[(chunkX, chunkZ)];
        var neighbors = new List<IChunk>();

        for (var z = chunkZ - 1; z <= chunkZ + 1; z++)
        {
            for (var x = chunkX - 1; x <= chunkX + 1; x++)
            {
                neighbors.Add(chunks[(x, z)]);
            }
        }

        var random = this.terrain.Compiler.RandomFactory("minecraft:worldgen_region_random")
            .At(chunkX << 4, 0, chunkZ << 4);
        var region = new WorldGenRegion(chunks, chunkX, chunkZ, this.terrain.Seed, center.MinY, center.Height,
            this.terrain.Settings.SeaLevel, this.Source, random, trackHeightmaps: true)
        {
            UnlitBrightness = this.unlitBrightness
        };
        this.Decoration.Generate(region, center, neighbors, this.terrain.Settings.MinY, this.terrain.Settings.Height);

        return region;
    }

    public string BiomeAt(int x, int y, int z) => this.manager.GetBiome(x, y, z).Name;

    public string[] CarversAt(int chunkX, int chunkZ)
    {
        var biome = this.Source.GetNoiseBiome(chunkX << 2, 0, chunkZ << 2);
        var data = this.terrain.Data.Read("biome", biome.Name);
        var carvers = data.GetProperty("carvers");

        if (carvers.ValueKind == JsonValueKind.String)
        {
            return [carvers.GetString()!];
        }

        return carvers.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    public void Generate(IChunk chunk, ICollection<Vector>? fluidUpdates = null)
    {
        this.Source.FillBiomes(chunk);
        fluidUpdates ??= (chunk as Chunk)?.PostProcessing;
        this.terrain.Generate(chunk, this.BiomeAt, this.CarversAt, fluidUpdates);

        // Chunk storage keeps the legacy enum until the live pipeline switches; carvers saves as 26.3 terrain.
        chunk.SetChunkStatus(ChunkGenStage.carvers);
    }
}
