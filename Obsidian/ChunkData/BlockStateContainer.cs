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

        if (!this.IsSingleValued)
            this.DataArray = new(this.MinBitsPerEntry, this.MaxEntryCount);

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

        var data = this.DataArray;
        if (data is null)
            return 0;

        int airIndex = this.Palette.TryGetId(BlocksRegistry.Air, out var air) ? air : -1;
        int caveAirIndex = this.Palette.TryGetId(BlocksRegistry.CaveAir, out air) ? air : -1;
        int voidAirIndex = this.Palette.TryGetId(BlocksRegistry.VoidAir, out air) ? air : -1;

        // If no air variants exist in the palette, then all entries are non-air.
        if (airIndex < 0 && caveAirIndex < 0 && voidAirIndex < 0)
            return (short)this.MaxEntryCount;

        int count = 0;

        for (int i = 0; i < this.MaxEntryCount; i++)
        {
            int index = data[i];
            if (index != airIndex && index != caveAirIndex && index != voidAirIndex)
                count++;
        }

        return (short)count;
    }

    public override BlockStateContainer Clone() => new(Palette.Clone(), DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 4 | z) << 4 | x;
}
