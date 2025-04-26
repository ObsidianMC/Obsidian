namespace Obsidian.API.ChunkData;
public interface IChunkSection
{
    public int? YBase { get; }

    public DataContainer<IBlock> BlockStateContainer { get; }
    public DataContainer<Biome> BiomeContainer { get; }

    public bool HasSkyLight { get;  }
    public ReadOnlyMemory<byte> SkyLightArray { get; }

    public bool HasBlockLight { get; } 
    public ReadOnlyMemory<byte> BlockLightArray { get; }

    public bool IsEmpty { get; }

    public IBlock GetBlock(Vector position);
    public IBlock GetBlock(int x, int y, int z);

    public Biome GetBiome(Vector position);
    public Biome GetBiome(int x, int y, int z);

    public void SetBlock(Vector position, IBlock block);
    public void SetBlock(int x, int y, int z, IBlock block);

    public void SetBiome(Vector position, Biome biome);
    public void SetBiome(int x, int y, int z, Biome biome);

    public void SetLightLevel(Vector position, LightType lt, int level);
    public void SetLightLevel(int x, int y, int z, LightType lt, int level);

    public int GetLightLevel(Vector position, LightType lt);
    public int GetLightLevel(int x, int y, int z, LightType lt);


    public void SetLight(byte[] data, LightType lt);
    public IChunkSection Clone();
}
