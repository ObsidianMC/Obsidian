using System.Diagnostics;

namespace Obsidian.ChunkData;

public sealed class ChunkSection
{
    public bool Overworld = true;

    public int? YBase { get; }

    public BlockStateContainer BlockStateContainer { get; }
    public BiomeContainer BiomeContainer { get; }

    public bool HasSkyLight { get; private set; } = false;
    public ReadOnlyMemory<byte> SkyLightArray => skyLight.AsMemory();

    public bool HasBlockLight { get; private set; } = false;
    public ReadOnlyMemory<byte> BlockLightArray => blockLight.AsMemory();

    private byte[] skyLight = new byte[2048];

    private byte[] blockLight = new byte[2048];

    public ChunkSection(byte bitsPerBlock = 4, byte bitsPerBiome = 2, int? yBase = null)
    {
        this.BlockStateContainer = new(bitsPerBlock);
        this.BiomeContainer = new(bitsPerBiome);

        this.YBase = yBase;

        int airIndex = BlockStateContainer.Palette.GetOrAddId(Block.Air);
        Debug.Assert(airIndex == 0);
    }

    private ChunkSection(BlockStateContainer blockContainer, BiomeContainer biomeContainer, int? yBase)
    {
        BlockStateContainer = blockContainer;
        BiomeContainer = biomeContainer;
        YBase = yBase;
    }

    public Block GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);
    public Block GetBlock(int x, int y, int z) => this.BlockStateContainer.Get(x, y, z);

    public Biomes GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);
    public Biomes GetBiome(int x, int y, int z) => this.BiomeContainer.Get(x, y, z);

    public void SetBlock(Vector position, Block block) => this.SetBlock(position.X, position.Y, position.Z, block);
    public void SetBlock(int x, int y, int z, Block block) => this.BlockStateContainer.Set(x, y, z, block);

    public void SetBiome(Vector position, Biomes biome) => this.SetBiome(position.X, position.Y, position.Z, biome);
    public void SetBiome(int x, int y, int z, Biomes biome) => this.BiomeContainer.Set(x, y, z, biome);

    public void SetSkyLight(Vector position, int light) => this.SetSkyLight(position.X, position.Y, position.Z, light);
    public void SetSkyLight(int x, int y, int z, int light)
    {
        var index = (y << 8) | (z << 4) | x;
        // each value is 4 bits. So upper 4 bits will be odd, lower even
        light <<= (index & 1) << 2;
        index /= 2;
        skyLight[index] |= (byte)light;
        HasSkyLight = true;
    }

    public void SetBlockLight(Vector position, int light) => this.SetBlockLight(position.X, position.Y, position.Z, light);
    public void SetBlockLight(int x, int y, int z, int light)
    {
        var index = (y << 8) | (z << 4) | x;
        light <<= (index & 1) << 2;
        index /= 2;
        blockLight[index] |= (byte)light;
        HasBlockLight = true;
    }

    public int GetLightLevel(Vector position) => GetLightLevel(position.X, position.Y, position.Z);
    public int GetLightLevel(int x, int y, int z)
    {
        var index = (y << 8) | (z << 4) | x;
        var mask = index % 2 == 0 ? 0x0F : 0xF0;
        index /= 2;
        return (skyLight[index] & mask) | (blockLight[index] & mask);
    }

    public ChunkSection Clone()
    {
        return new ChunkSection(BlockStateContainer.Clone(), BiomeContainer.Clone(), YBase);
    }
}
