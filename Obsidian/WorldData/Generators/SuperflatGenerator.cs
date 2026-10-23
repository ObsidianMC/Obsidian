using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Lighting;
using System.Threading;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// Generates flat terrain: the same layers of blocks in every column, from the bottom of the level up, in one biome.
/// The <c>superflat</c> generator is Obsidian's own terrain (bedrock, 3 dirt and grass, in plains); a
/// <c>minecraft:flat</c> level stem's level gets its own layers (see <see cref="FlatStem"/>).
/// </summary>
public class SuperflatGenerator : ILevelGenerator
{
    // The layers VanillaLevelData matches flat worlds' level.dat against.
    private static readonly FlatLevelSettings superflat = new(
        [
            BlocksRegistry.Bedrock,
            BlocksRegistry.Dirt,
            BlocksRegistry.Dirt,
            BlocksRegistry.Dirt,
            BlocksRegistry.GrassBlock
        ],
        CodecRegistry.Biomes.Plains);

    private FlatLevelSettings settings;

    // Every chunk is a clone of this one, built for the build range of the chunks asked for.
    private Chunk? model;

    private ILevel? level;

    /// <summary>
    /// Obsidian's superflat terrain.
    /// </summary>
    public SuperflatGenerator()
        : this(superflat)
    {
    }

    /// <param name="settings">The layers and biome.</param>
    internal SuperflatGenerator(FlatLevelSettings settings)
    {
        this.settings = settings;
    }

    public virtual string Id => "superflat";

    /// <summary>
    /// The layers and biome. Setting them, before the level generates, drops the chunk built with the previous ones.
    /// </summary>
    internal FlatLevelSettings Settings
    {
        get => this.settings;
        set
        {
            this.settings = value;
            Volatile.Write(ref this.model, null);
        }
    }

    public virtual void Init(ILevel level) => this.level = level;

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage status = ChunkGenStage.full)
    {
        if (chunk is { IsGenerated: true })
            return ValueTask.FromResult(chunk);

        var minY = chunk?.MinY ?? OverworldBuildRange.MinY;
        var height = chunk?.Height ?? OverworldBuildRange.Height;

        var model = Volatile.Read(ref this.model);
        if (model is null || model.MinY != minY || model.Height != height)
        {
            model = this.CreateModel(minY, height);
            Volatile.Write(ref this.model, model);
        }

        return ValueTask.FromResult(model.Clone(x, z));
    }

    /// <summary>
    /// Players spawn at the world's center, on top of the layers, like vanilla's FlatLevelSource.getSpawnHeight.
    /// </summary>
    public ValueTask<VectorD?> FindSpawnPointAsync()
    {
        var range = this.level?.DimensionType.Element;
        var minY = range?.MinY ?? OverworldBuildRange.MinY;
        var height = range?.Height ?? OverworldBuildRange.Height;

        var top = minY + Math.Min(height, this.settings.Layers.Length);

        return ValueTask.FromResult<VectorD?>(new VectorD(0.5, top, 0.5));
    }

    // Builds the chunk every generated chunk is cloned from. Its columns are all the same, so lighting it on its own
    // lights it like its neighbors would.
    private Chunk CreateModel(int minY, int height)
    {
        var chunk = new Chunk(0, 0, minY, height);
        var layers = this.settings.Layers;

        // Like vanilla's FlatLevelSource.fillFromNoise: layers fill from the bottom of the level, and those above its
        // top are cut.
        for (var layer = 0; layer < Math.Min(height, layers.Length); layer++)
        {
            if (layers[layer].RegistryId == BlockIds.Air)
                continue;

            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                    chunk.SetBlock(x, minY + layer, z, layers[layer]);
            }
        }

        // Biomes are stored per 4x4x4 cell.
        for (var x = 0; x < 16; x += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var y = minY; y < minY + height; y += 4)
                    chunk.SetBiome(x, y, z, this.settings.Biome);
            }
        }

        WorldgenHeightmaps.UpdateFinal(chunk, minY, height);
        LightEngine.LightChunk(chunk, [], this.level?.DimensionType.Element.HasSkylight ?? true);
        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }
}

/// <summary>
/// The terrain of a flat level, like vanilla's FlatLevelGeneratorSettings.
/// </summary>
/// <param name="Layers">The block of each layer from the bottom of the level up, one per block of height.</param>
/// <param name="Biome">The biome everywhere.</param>
internal sealed record FlatLevelSettings(ImmutableArray<IBlock> Layers, BiomeCodec Biome);
