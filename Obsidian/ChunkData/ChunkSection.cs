using System.Diagnostics;

namespace Obsidian.ChunkData;

public sealed class ChunkSection
{
    public bool Overworld = true;

    public int? YBase { get; }

    public BlockStateContainer BlockStateContainer { get; }
    public BiomeContainer BiomeContainer { get; }

    public ChunkSection(byte bitsPerBlock = 4, byte bitsPerBiome = 2, int? yBase = null)
    {
        this.BlockStateContainer = new(bitsPerBlock);
        this.BiomeContainer = new(bitsPerBiome);

        this.YBase = yBase;

        int airIndex = BlockStateContainer.Palette.GetOrAddId(Block.Air);
        Debug.Assert(airIndex == 0);
    }

    public Block GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);
    public Block GetBlock(int x, int y, int z) => this.BlockStateContainer.Get(x, y, z);

    public Biomes GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);
    public Biomes GetBiome(int x, int y, int z) => this.BiomeContainer.Get(x, y, z);

    public void SetBlock(Vector position, Block block) => this.SetBlock(position.X, position.Y, position.Z, block);
    public void SetBlock(int x, int y, int z, Block block) => this.BlockStateContainer.Set(x, y, z, block);

    public void SetBiome(Vector position, Biomes biome) => this.SetBiome(position.X, position.Y, position.Z, biome);
    public void SetBiome(int x, int y, int z, Biomes biome) => this.BiomeContainer.Set(x, y, z, biome);
}
