using Obsidian.API.ChunkData;
using Obsidian.API.Registry.Codecs.Biomes;
using System.ComponentModel;

namespace Obsidian.API;
public interface IChunk
{
    public int X { get; }

    public int Z { get; }

    public bool IsGenerated { get; }

    public ChunkGenStage ChunkStatus { get; }

    /// <summary>
    /// The lowest block Y of the chunk, i.e. its dimension's minimum build height.
    /// </summary>
    public int MinY { get; }

    /// <summary>
    /// The number of block layers in the chunk, i.e. its dimension's build height.
    /// </summary>
    public int Height { get; }

    public IDictionary<HeightmapType, Heightmap> Heightmaps { get; }

    /// <summary>
    /// The chunk's sections from the bottom up. This is a view of the chunk's own sections, not a copy.
    /// </summary>
    public ReadOnlySpan<IChunkSection> Sections { get; }

    public IBlock GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);

    public IBlock GetBlock(int x, int y, int z);
    public BiomeCodec GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);

    public BiomeCodec GetBiome(int x, int y, int z);

    public void SetBiome(Vector position, BiomeCodec biome) => this.SetBiome(position.X, position.Y, position.Z, biome);

    public void SetBiome(int x, int y, int z, BiomeCodec biome);

    public void SetBlock(Vector position, IBlock block) => this.SetBlock(position.X, position.Y, position.Z, block);

    public void SetBlock(int x, int y, int z, IBlock block);
    public void SetLightLevel(Vector position, LightType lt, int light) => this.SetLightLevel(position.X, position.Y, position.Z, lt, light);
    public void SetLightLevel(int x, int y, int z, LightType lt, int level);

    public int GetLightLevel(Vector position, LightType lt) => this.GetLightLevel(position.X, position.Y, position.Z, lt);
    public int GetLightLevel(int x, int y, int z, LightType lt);

    public IBlockEntity GetBlockEntity(int x, int y, int z);
    public void SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData);
    public void RemoveBlockEntity(int x, int y, int z);

    /// <summary>
    /// Every block entity in the chunk.
    /// </summary>
    public IReadOnlyCollection<IBlockEntity> GetBlockEntities();

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetChunkStatus(ChunkGenStage status);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteLightMaskTo(INetStreamWriter writer, LightType lt);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteEmptyLightMaskTo(INetStreamWriter writer, LightType lt);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteLightTo(INetStreamWriter writer, LightType lt);

    public IChunk Clone(int x, int z);
}
