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
        BlockStateContainer = new(bitsPerBlock);
        BiomeContainer = new(bitsPerBiome);

        YBase = yBase;

        int airIndex = BlockStateContainer.Palette.GetOrAddId(Block.Air);
        Debug.Assert(airIndex == 0);
    }

    private ChunkSection(BlockStateContainer blockContainer, BiomeContainer biomeContainer, int? yBase)
    {
        BlockStateContainer = blockContainer;
        BiomeContainer = biomeContainer;
        YBase = yBase;
    }

    public Block GetBlock(Vector position) => GetBlock(position.X, position.Y, position.Z);
    public Block GetBlock(int x, int y, int z) => BlockStateContainer.Get(x, y, z);

    public Biomes GetBiome(Vector position) => GetBiome(position.X, position.Y, position.Z);
    public Biomes GetBiome(int x, int y, int z) => BiomeContainer.Get(x, y, z);

    public void SetBlock(Vector position, Block block) => SetBlock(position.X, position.Y, position.Z, block);
    public void SetBlock(int x, int y, int z, Block block) => BlockStateContainer.Set(x, y, z, block);

    public void SetBiome(Vector position, Biomes biome) => SetBiome(position.X, position.Y, position.Z, biome);
    public void SetBiome(int x, int y, int z, Biomes biome) => BiomeContainer.Set(x, y, z, biome);

    public ChunkSection Clone()
    {
        return new ChunkSection(BlockStateContainer.Clone(), BiomeContainer.Clone(), YBase);
    }
}
