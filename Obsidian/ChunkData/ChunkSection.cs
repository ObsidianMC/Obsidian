namespace Obsidian.ChunkData;

public sealed class ChunkSection
{
    public bool Overworld = true;

    public int? YBase { get; }

    public BlockStateContainer BlockStateContainer { get; }
    public BiomeContainer BiomeContainer { get; }

    public short BlockCount { get; private set; } = 0;

    public ChunkSection(byte bitsPerBlock = 6, byte bitsPerBiome = 3, int? yBase = null)
    {
        this.BlockStateContainer = new(bitsPerBlock);
        this.BiomeContainer = new(bitsPerBiome);

        this.YBase = yBase;

        this.FillWithAir();
    }

    public Block GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);
    public Block GetBlock(int x, int y, int z) => this.BlockStateContainer.Get(x, y, z);

    public Biomes GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);
    public Biomes GetBiome(int x, int y, int z) => this.BiomeContainer.Get(x, y, z);

    public bool SetBlock(Vector position, Block block) => this.SetBlock(position.X, position.Y, position.Z, block);
    public bool SetBlock(int x, int y, int z, Block block)
    {
        BlockCount++;
        return this.BlockStateContainer.Set(x, y, z, block);
    }

    public bool SetBiome(Vector position, Biomes biome) => this.SetBiome(position.X, position.Y, position.Z, biome);
    public bool SetBiome(int x, int y, int z, Biomes biome) => this.BiomeContainer.Set(x, y, z, biome);

    private void FillWithAir()
    {
        var air = Block.Air;
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    this.BlockStateContainer.Set(x, y, z, air);
                }
            }
        }
    }
}
