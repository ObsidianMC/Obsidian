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

    public IBlock GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);
    public IBlock GetBlock(int x, int y, int z);

    public Biome GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);
    public Biome GetBiome(int x, int y, int z);

    public void SetBlock(Vector position, IBlock block) => this.SetBlock(position.X, position.Y, position.Z, block);
    public void SetBlock(int x, int y, int z, IBlock block);

    public void SetBiome(Vector position, Biome biome) => this.SetBiome(position.X, position.Y, position.Z, biome);
    public void SetBiome(int x, int y, int z, Biome biome);

    public void SetLightLevel(Vector position, LightType lt, int level) => this.SetLightLevel(position.X, position.Y, position.Z, lt, level);
    public void SetLightLevel(int x, int y, int z, LightType lt, int level);

    public int GetLightLevel(Vector position, LightType lt) => GetLightLevel(position.X, position.Y, position.Z, lt);
    public int GetLightLevel(int x, int y, int z, LightType lt);


    public void SetLight(byte[] data, LightType lt);
    public IChunkSection Clone();
}
