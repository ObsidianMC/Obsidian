namespace Obsidian.API;
public interface IChunk
{
    public int X { get; }

    public int Z { get; }

    public IReadOnlyDictionary<HeightmapType, Heightmap> Heightmaps { get; }

    public IBlock GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);

    public IBlock GetBlock(int x, int y, int z);
    public Biome GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);

    public Biome GetBiome(int x, int y, int z);

    public void SetBiome(Vector position, Biome biome) => this.SetBiome(position.X, position.Y, position.Z, biome);

    public void SetBiome(int x, int y, int z, Biome biome);

    public void SetBlock(Vector position, IBlock block) => this.SetBlock(position.X, position.Y, position.Z, block);

    public void SetBlock(int x, int y, int z, IBlock block);
    public void SetLightLevel(Vector position, LightType lt, int light) => this.SetLightLevel(position.X, position.Y, position.Z, lt, light);
    public void SetLightLevel(int x, int y, int z, LightType lt, int level);

    public int GetLightLevel(Vector position, LightType lt) => this.GetLightLevel(position.X, position.Y, position.Z, lt);
    public int GetLightLevel(int x, int y, int z, LightType lt);

    public IChunk Clone(int x, int z);
}
