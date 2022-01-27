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

    public bool IsEmpty { get; private set; } = true;

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
    public void SetBlock(int x, int y, int z, Block block)
    {
        if (!block.IsAir)
            IsEmpty = false;
        this.BlockStateContainer.Set(x, y, z, block);
    }

    public void SetBiome(Vector position, Biomes biome) => this.SetBiome(position.X, position.Y, position.Z, biome);
    public void SetBiome(int x, int y, int z, Biomes biome) => this.BiomeContainer.Set(x, y, z, biome);

    public void SetLightLevel(Vector position, LightType lt, int level) => this.SetLightLevel(position.X, position.Y, position.Z, lt, level);
    public void SetLightLevel(int x, int y, int z, LightType lt, int level)
    {
        // each value is 4 bits. So upper 4 bits will be odd, lower even
        var index = (y << 8) | (z << 4) | x;
        int shift = (index & 1) << 2;
        index /= 2;
        var data = lt == LightType.Sky ? skyLight : blockLight;
        data[index] &= (byte)(0xF0 >> shift);
        data[index] |= (byte)(level << shift);
        HasSkyLight |= lt == LightType.Sky;
        HasBlockLight |= lt == LightType.Block;
    }

    public int GetLightLevel(Vector position, LightType lt) => GetLightLevel(position.X, position.Y, position.Z, lt);
    public int GetLightLevel(int x, int y, int z, LightType lt)
    {
        var index = (y << 8) | (z << 4) | x;
        var shift = (index & 1) << 2;
        var mask = 0xF << shift;
        index /= 2;
        return lt == LightType.Sky ? (skyLight[index] & mask) >> shift : (blockLight[index] & mask >> shift);
    }

    internal void SetLight(byte[] data, LightType lt)
    {
        foreach (var b in data)
        {
            if (b != 0)
            {
                if (lt == LightType.Sky)
                    HasSkyLight = true;
                else
                    HasBlockLight = true;
                break;
            }
        }
        if (lt == LightType.Sky)
            skyLight = data;
        else
            blockLight = data;
        
    }

    public ChunkSection Clone()
    {
        return new ChunkSection(BlockStateContainer.Clone(), BiomeContainer.Clone(), YBase);
    }
}
