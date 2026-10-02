using Obsidian.API.Registry.Codecs.Biomes;
using System.Diagnostics;

namespace Obsidian.ChunkData;

public sealed class ChunkSection : IChunkSection
{
    public int? YBase { get; }

    public DataContainer<IBlock> BlockStateContainer { get; }
    public DataContainer<BiomeCodec> BiomeContainer { get; }

    public bool HasSkyLight { get; private set; }
    public ReadOnlyMemory<byte> SkyLightArray => skyLight.AsMemory();

    public bool HasBlockLight { get; private set; }
    public ReadOnlyMemory<byte> BlockLightArray => blockLight.AsMemory();

    public bool IsEmpty { get; private set; } = true;

    private static readonly byte[] FullSkyLight = new byte[2048];

    // SetBlock counts a section empty until it holds a block other than air (only plain air).
    private static readonly int airStateId = BlocksRegistry.Air.GetHashCode();

    static ChunkSection()
    {
        Array.Fill(FullSkyLight, (byte)0xFF); // 0xFF = two 15s packed together
    }

    private byte[] skyLight = new byte[2048];

    private byte[] blockLight = new byte[2048];

    public ChunkSection(byte bitsPerBlock = 0, byte bitsPerBiome = 0, int? yBase = null)
    {
        this.BlockStateContainer = new BlockStateContainer(bitsPerBlock);
        this.BiomeContainer = new BiomeContainer(bitsPerBiome);

        this.YBase = yBase;

        int airIndex = BlockStateContainer.Palette.GetOrAddId(BlocksRegistry.Air);
        Debug.Assert(airIndex == 0);
    }

    private ChunkSection(DataContainer<IBlock> blockContainer, DataContainer<BiomeCodec> biomeContainer, int? yBase, bool isEmpty)
    {
        BlockStateContainer = blockContainer;
        BiomeContainer = biomeContainer;
        YBase = yBase;
        IsEmpty = isEmpty;
    }

    /// <summary>
    /// Recomputes <see cref="IsEmpty"/> from the blocks, for sections whose block storage was filled directly (loading).
    /// </summary>
    public void RecalculateEmpty()
    {
        for (var y = 0; y < 16; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    if (this.GetBlock(x, y, z).Material != Material.Air)
                    {
                        this.IsEmpty = false;
                        return;
                    }
                }
            }
        }

        this.IsEmpty = true;
    }

    public IBlock GetBlock(int x, int y, int z) => this.BlockStateContainer.Get(x, y, z);

    public BiomeCodec GetBiome(int x, int y, int z) => this.BiomeContainer.Get(x, y, z);
    public void SetBlock(int x, int y, int z, IBlock block)
    {
        if (block.Material != Material.Air)
            IsEmpty = false;

        this.BlockStateContainer.Set(x, y, z, block);
    }
    /// <summary>
    /// Sets the blocks of layer <paramref name="y"/>: entry <c>z * 16 + x</c> of <paramref name="layer"/> indexes
    /// <paramref name="blocks"/>, where null blocks are skipped. Same as calling <see cref="SetBlock(int, int, int, IBlock)"/>
    /// for each block in index order, but much faster.
    /// </summary>
    internal void SetBlockLayer(int y, ReadOnlySpan<byte> layer, ReadOnlySpan<IBlock?> blocks)
    {
        if (IsEmpty)
        {
            foreach (var index in layer)
            {
                if (blocks[index] is { } block && block.Material != Material.Air)
                {
                    IsEmpty = false;
                    break;
                }
            }
        }

        if (this.BlockStateContainer is BlockStateContainer container)
        {
            container.SetLayer(y, layer, blocks);
            return;
        }

        for (var i = 0; i < 256; i++)
        {
            if (blocks[layer[i]] is { } block)
                this.BlockStateContainer.Set(i & 15, y, i >> 4, block);
        }
    }

    public void SetBiome(int x, int y, int z, BiomeCodec biome) => this.BiomeContainer.Set(x, y, z, biome);

    /// <summary>
    /// The state id of the block at the section's local position, without resolving the block.
    /// </summary>
    public int GetStateId(int x, int y, int z) => ((BlockStateContainer)this.BlockStateContainer).GetStateId(x, y, z);

    /// <summary>
    /// Sets the block at the section's local position by its state id, like <see cref="SetBlock(int, int, int, IBlock)"/>.
    /// </summary>
    public void SetStateId(int x, int y, int z, int stateId)
    {
        if (stateId != airStateId)
            IsEmpty = false;

        ((BlockStateContainer)this.BlockStateContainer).SetStateId(x, y, z, stateId);
    }
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

    /// <summary>
    /// The section's light levels of a type, two per byte as <see cref="SetLightLevel(int, int, int, LightType, int)"/>
    /// stores them, for the light engine to read and write directly. Call <see cref="MarkLight"/> after writing.
    /// </summary>
    internal byte[] GetLightStorage(LightType lt) => lt == LightType.Sky ? skyLight : blockLight;

    /// <summary>
    /// Records that light of a type was written into <see cref="GetLightStorage"/>, like
    /// <see cref="SetLightLevel(int, int, int, LightType, int)"/> does.
    /// </summary>
    internal void MarkLight(LightType lt)
    {
        HasSkyLight |= lt == LightType.Sky;
        HasBlockLight |= lt == LightType.Block;
    }

    public int GetLightLevel(int x, int y, int z, LightType lt)
    {
        var index = (y << 8) | (z << 4) | x;
        var shift = (index & 1) << 2;
        var mask = 0xF << shift;
        index /= 2;
        return ((lt == LightType.Sky ? skyLight : blockLight)[index] & mask) >> shift;
    }

    public void SetLight(byte[] data, LightType lt)
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

    public void FillSkyLight()
    {
        Array.Copy(FullSkyLight, skyLight, 2048);
        HasSkyLight = true;
    }

    public IBlock GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);
    public BiomeCodec GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);
    public void SetBlock(Vector position, IBlock block) => this.SetBlock(position.X, position.Y, position.Z, block);
    public void SetBiome(Vector position, BiomeCodec biome) => this.SetBiome(position.X, position.Y, position.Z, biome);
    public void SetLightLevel(Vector position, LightType lt, int level) => this.SetLightLevel(position.X, position.Y, position.Z, lt, level);
    public int GetLightLevel(Vector position, LightType lt) => this.GetLightLevel(position.X, position.Y, position.Z, lt);

    public IChunkSection Clone() => new ChunkSection(BlockStateContainer.Clone(), BiomeContainer.Clone(), YBase, IsEmpty);
}
