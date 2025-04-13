namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : DataContainer<IBlock>
{
    public override IPalette<IBlock> Palette { get; internal set; }

    public bool IsEmpty => DataArray.storage.Length == 0;

    internal override DataArray DataArray { get; private protected set; }


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

    public override void Set(int x, int y, int z, IBlock blockState)
    {
#if CACHE_VALID_BLOCKS
        validBlockCount.SetDirty();
#endif
        var blockIndex = GetIndex(x, y, z);

        int paletteId = Palette.GetOrAddId(blockState);

        this.GrowDataArray();

        DataArray[blockIndex] = paletteId;
    }

    public override IBlock Get(int x, int y, int z)
    {
        int storageId = DataArray[GetIndex(x, y, z)];

        return Palette.GetValueFromIndex(storageId);
    }

    public override void WriteTo(INetStreamWriter writer)
    {
#if CACHE_VALID_BLOCKS
        var validBlocks = validBlockCount.GetValue();
#else
        var validBlocks = GetNonAirBlocks();
#endif

        writer.WriteShort(validBlocks);
        writer.WriteByte(BitsPerEntry);

        Palette.WriteTo(writer);

        writer.WriteVarInt(DataArray.storage.Length);
        writer.WriteLongArray(DataArray.storage);
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

    private short GetNonAirBlocks()
    {
        int validBlocksCount = 0;

        if (!Palette.TryGetId(BlocksRegistry.Air, out var indexOne))
            goto NO_AIR;
        if (!Palette.TryGetId(BlocksRegistry.CaveAir, out var indexTwo))
            goto NO_CAVE;
        if (!Palette.TryGetId(BlocksRegistry.VoidAir, out var indexThree))
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
        if (!Palette.TryGetId(BlocksRegistry.CaveAir, out indexOne))
            goto NO_AIR_CAVE;
        if (!Palette.TryGetId(BlocksRegistry.VoidAir, out indexTwo))
            goto ONE_INDEX;
        goto TWO_INDEXES;

    // 1 0 ?
    NO_CAVE:
        if (!Palette.TryGetId(BlocksRegistry.VoidAir, out indexTwo))
            goto ONE_INDEX;
        goto TWO_INDEXES;

    // 0 0 ?
    NO_AIR_CAVE:
        if (!Palette.TryGetId(BlocksRegistry.VoidAir, out indexOne))
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

    public override BlockStateContainer Clone() => new(Palette.Clone(), DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 4 | z) << 4 | x;
}
