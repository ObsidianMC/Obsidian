namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : DataContainer<IBlock>
{
    public override IPalette<IBlock> Palette { get; internal set; }

    public override bool IsEmpty => this.IsSingleValued ? !this.Palette.IsFull : DataArray.storage.Length == 0;

    internal override DataArray? DataArray { get; private protected set; }


#if CACHE_VALID_BLOCKS
    private readonly DirtyCache<short> validBlockCount;
#endif

    internal BlockStateContainer(byte bitsPerEntry = 0) : base(4, 8, 4096, ChunkData.PaletteFactory.DetermineBlockPalette)
    {
        Palette = this.PaletteFactory(bitsPerEntry);

#if CACHE_VALID_BLOCKS
        validBlockCount = new(GetNonAirBlocks);
#endif
    }

    private BlockStateContainer(IPalette<IBlock> palette, DataArray? dataArray) : base(4, 8, 4096, ChunkData.PaletteFactory.DetermineBlockPalette)
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
        base.Set(x, y, z, blockState);
    }

    public override void WriteTo(INetStreamWriter writer)
    {
#if CACHE_VALID_BLOCKS
        var validBlocks = validBlockCount.GetValue();
#else
        var validBlocks = GetNonAirBlocks();
#endif

        writer.WriteShort(validBlocks);

        base.WriteTo(writer);
    }

    private short GetNonAirBlocks()
    {
        if (this.Palette is SingleValuePalette<IBlock> singleValuePalette)
            return singleValuePalette.Value.IsAir ? (short)0 : (short)this.MaxEntryCount;

        int validBlocksCount = 0;

        if (!Palette.TryGetId(BlocksRegistry.Air, out var indexOne))
            goto NO_AIR;
        if (!Palette.TryGetId(BlocksRegistry.CaveAir, out var indexTwo))
            goto NO_CAVE;
        if (!Palette.TryGetId(BlocksRegistry.VoidAir, out var indexThree))
            goto TWO_INDEXES;

        // 1 1 1
        for (int i = 0; i < this.MaxEntryCount; i++)
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
        for (int i = 0; i < this.MaxEntryCount; i++)
        {
            int index = DataArray[i];
            if (index != indexOne)
                validBlocksCount++;
        }
        return (short)validBlocksCount;

    // 1 1 0
    TWO_INDEXES:
        for (int i = 0; i < this.MaxEntryCount; i++)
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
