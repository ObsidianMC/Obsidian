using Obsidian.Net;
using Obsidian.Utilities.Collection;
using Obsidian.Utilities.Registry;

namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : DataContainer<IBlock>
{
    public override IPalette<IBlock> Palette { get; internal set; }

    public bool IsEmpty => DataArray.storage.Length == 0;

    public override DataArray DataArray { get; protected set; }

#if CACHE_VALID_BLOCKS
    private readonly DirtyCache<short> validBlockCount;
#endif

    internal BlockStateContainer(byte bitsPerEntry = 4)
    {
        DataArray = new DataArray(bitsPerEntry, 4096);
        Palette = bitsPerEntry.DetermineBlockPalette();

#if CACHE_VALID_BLOCKS
        validBlockCount = new(GetNonAirBlocks);
#endif
    }

    private BlockStateContainer(IPalette<IBlock> palette, DataArray dataArray)
    {
        Palette = palette;
        DataArray = dataArray;

#if CACHE_VALID_BLOCKS
        validBlockCount = new(GetNonAirBlocks);
#endif
    }

    public void Set(int x, int y, int z, IBlock blockState)
    {
#if CACHE_VALID_BLOCKS
        validBlockCount.SetDirty();
#endif
        var blockIndex = GetIndex(x, y, z);

        int paletteId = Palette.GetOrAddId(blockState);

        if (Palette.BitCount > DataArray.BitsPerEntry)
            DataArray = DataArray.Grow(Palette.BitCount);

        DataArray[blockIndex] = paletteId;
    }

    internal void GrowDataArray()
    {
        if (Palette.BitCount > DataArray.BitsPerEntry)
            DataArray = DataArray.Grow(Palette.BitCount);
    }

    public IBlock Get(int x, int y, int z)
    {
        int storageId = DataArray[GetIndex(x, y, z)];

        return Palette.GetValueFromIndex(storageId);
    }

    public override async Task WriteToAsync(MinecraftStream stream)
    {
#if CACHE_VALID_BLOCKS
        var validBlocks = validBlockCount.GetValue();
#else
        var validBlocks = GetNonAirBlocks();
#endif

        await stream.WriteShortAsync(validBlocks);
        await stream.WriteUnsignedByteAsync(BitsPerEntry);

        await Palette.WriteToAsync(stream);

        await stream.WriteVarIntAsync(DataArray.storage.Length);
        await stream.WriteLongArrayAsync(DataArray.storage);
    }

    public override void WriteTo(MinecraftStream stream)
    {
#if CACHE_VALID_BLOCKS
        var validBlocks = validBlockCount.GetValue();
#else
        var validBlocks = GetNonAirBlocks();
#endif

        stream.WriteShort(validBlocks);
        stream.WriteUnsignedByte(BitsPerEntry);

        Palette.WriteTo(stream);

        stream.WriteVarInt(DataArray.storage.Length);
        stream.WriteLongArray(DataArray.storage);
    }

    public void Fill(IBlock block)
    {
#if CACHE_VALID_BLOCKS
        validBlockCount.SetDirty();
#endif
        int index = Palette.GetOrAddId(block);
        for (int i = 0; i < 16 * 16 * 16; i++)
        {
            DataArray[i] = index;
        }
    }

    private static readonly IBlock air = BlocksRegistry.Get(0);
    private static readonly IBlock caveAir = BlocksRegistry.Get(Material.CaveAir);
    private static readonly IBlock voidAir = BlocksRegistry.Get(Material.VoidAir);

    private short GetNonAirBlocks()
    {
        int validBlocksCount = 0;
        int indexOne, indexTwo, indexThree;

        if (!Palette.TryGetId(air, out indexOne))
            goto NO_AIR;
        if (!Palette.TryGetId(caveAir, out indexTwo))
            goto NO_CAVE;
        if (!Palette.TryGetId(voidAir, out indexThree))
            goto TWO_INDEXES;

        // 1 1 1
        for (int i = 0; i < 16 * 16 * 16; i++)
        {
            int index = DataArray[i];
            if (index != indexOne && index != indexTwo && index != indexThree)
                validBlocksCount++;
        }
        return (short)validBlocksCount;

    // 0 ? ?
    NO_AIR:
        if (!Palette.TryGetId(caveAir, out indexOne))
            goto NO_AIR_CAVE;
        if (!Palette.TryGetId(voidAir, out indexTwo))
            goto ONE_INDEX;
        goto TWO_INDEXES;

    // 1 0 ?
    NO_CAVE:
        if (!Palette.TryGetId(voidAir, out indexTwo))
            goto ONE_INDEX;
        goto TWO_INDEXES;

    // 0 0 ?
    NO_AIR_CAVE:
        if (!Palette.TryGetId(voidAir, out indexOne))
            return 0;
        // Fall through to ONE_INDEX

        // 1 0 0
        ONE_INDEX:
        for (int i = 0; i < 16 * 16 * 16; i++)
        {
            int index = DataArray[i];
            if (index != indexOne)
                validBlocksCount++;
        }
        return (short)validBlocksCount;

    // 1 1 0
    TWO_INDEXES:
        for (int i = 0; i < 16 * 16 * 16; i++)
        {
            int index = DataArray[i];
            if (index != indexOne && index != indexTwo)
                validBlocksCount++;
        }
        return (short)validBlocksCount;
    }

    public BlockStateContainer Clone()
    {
        return new BlockStateContainer(Palette.Clone(), DataArray.Clone());
    }

    public override int GetIndex(int x, int y, int z) => (y << 4 | z) << 4 | x;
}
