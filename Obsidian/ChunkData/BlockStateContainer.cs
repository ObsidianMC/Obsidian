namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : DataContainer<IBlock>
{
    private const int MaxEntryCount = 4096;
    public override IPalette<IBlock> Palette { get; internal set; }

    public override bool IsEmpty => this.Palette.Count == 0;

    internal BlockStateContainer(byte bitsPerEntry = 0) : base(MaxEntryCount, bitsPerEntry.DetermineBlockPalette(), BlocksRegistry.Air) { }

    private BlockStateContainer(IPalette<IBlock> palette, DataArray dataArray) : base(MaxEntryCount, palette)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    public override void WriteTo(INetStreamWriter writer)
    {
        var validBlocks = GetNonAirBlocks();

        writer.WriteShort(validBlocks);
        writer.WriteByte(BitsPerEntry);

        Palette.WriteTo(writer);

        if (this.DataArray != null)
            writer.WriteLongArray(DataArray.storage);
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
        for (int i = 0; i < MaxEntryCount; i++)
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
        for (int i = 0; i < MaxEntryCount; i++)
        {
            int index = DataArray[i];
            if (index != indexOne)
                validBlocksCount++;
        }
        return (short)validBlocksCount;

    // 1 1 0
    TWO_INDEXES:
        for (int i = 0; i < MaxEntryCount; i++)
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
