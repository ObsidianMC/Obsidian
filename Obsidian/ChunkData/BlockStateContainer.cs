namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : DataContainer<IBlock>
{
    public override IPalette<IBlock> Palette { get; internal set; }

    public override bool IsEmpty => this.IsSingleValued ? !this.Palette.IsFull : DataArray.storage.Length == 0;


#if CACHE_VALID_BLOCKS
    private readonly DirtyCache<short> validBlockCount;
#endif

    internal BlockStateContainer(byte bitsPerEntry = 0) : base(bitsPerEntry, 4, 8, 4096, ChunkData.PaletteFactory.DetermineBlockPalette)
    {
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
        lock (this.dataLock)
        {
            // The common case, an indirect palette, without the general path's checks.
            var data = this.DataArray;
            if (data is not null && this.Palette is IndirectBlockPalette palette)
            {
                var id = palette.GetOrAddId(blockState);
                if (palette.BitCount > data.BitsPerEntry)
                    this.DataArray = data = data.Grow(palette.BitCount);

                data[this.GetIndex(x, y, z)] = id;
                return;
            }

            base.Set(x, y, z, blockState);
        }
    }

    /// <remarks>
    /// Reads don't take the container's lock, since generation reads blocks far more than it writes them. Palettes only
    /// append and data arrays are replaced whole when they grow, so a read racing a write sees the block before or after
    /// it. The data array is read before the palette: it's set last when a single value palette grows. A read that finds
    /// them out of step (or a global palette) reads under the lock instead.
    /// </remarks>
    public override IBlock Get(int x, int y, int z)
    {
        var data = this.DataArray;
        var palette = this.Palette;

        if (data is not null)
        {
            if (palette is IndirectBlockPalette indirect && indirect.TryGetBlock(data[this.GetIndex(x, y, z)], out var block))
                return block;
        }
        else if (palette is SingleBlockValuePalette single && single.IsFull)
        {
            return single.Value;
        }

        return base.Get(x, y, z);
    }

    /// <summary>
    /// The state id of a block, read like <see cref="Get"/> without resolving the block.
    /// </summary>
    public int GetStateId(int x, int y, int z)
    {
        var data = this.DataArray;
        var palette = this.Palette;

        if (data is not null)
        {
            if (palette is IndirectBlockPalette indirect && indirect.TryGetStateId(data[this.GetIndex(x, y, z)], out var stateId))
                return stateId;
        }
        else if (palette is SingleBlockValuePalette single && single.IsFull)
        {
            return single.Value.GetHashCode();
        }

        return base.Get(x, y, z).GetHashCode();
    }

    /// <summary>
    /// Sets a block by its state id, like <see cref="Set"/>.
    /// </summary>
    public void SetStateId(int x, int y, int z, int stateId)
    {
#if CACHE_VALID_BLOCKS
        validBlockCount.SetDirty();
#endif
        lock (this.dataLock)
        {
            var data = this.DataArray;
            if (data is not null && this.Palette is IndirectBlockPalette palette)
            {
                var id = palette.GetOrAddValueId(stateId);
                if (palette.BitCount > data.BitsPerEntry)
                    this.DataArray = data = data.Grow(palette.BitCount);

                data[this.GetIndex(x, y, z)] = id;
                return;
            }

            base.Set(x, y, z, BlocksRegistry.Get(stateId));
        }
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

    public override BlockStateContainer Clone() => new(this.Palette.Clone(), this.IsSingleValued ? null : this.DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 4 | z) << 4 | x;
}
