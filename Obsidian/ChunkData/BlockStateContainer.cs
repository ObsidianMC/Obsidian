namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : DataContainer<IBlock>
{
    public override IPalette<IBlock> Palette { get; internal set; }

    public override bool IsEmpty => this.IsSingleValued ? !this.Palette.IsFull : DataArray.storage.Length == 0;


#if CACHE_VALID_BLOCKS
    private readonly DirtyCache<(short Blocks, short Fluids)> validBlockCount;
#endif

    internal BlockStateContainer(byte bitsPerEntry = 0) : base(bitsPerEntry, 4, 8, 4096, ChunkData.PaletteFactory.DetermineBlockPalette)
    {
#if CACHE_VALID_BLOCKS
        validBlockCount = new(CountBlocks);
#endif
    }

    private BlockStateContainer(IPalette<IBlock> palette, DataArray? dataArray) : base(4, 8, 4096, ChunkData.PaletteFactory.DetermineBlockPalette)
    {
        Palette = palette;
        DataArray = dataArray;

#if CACHE_VALID_BLOCKS
        validBlockCount = new(CountBlocks);
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
                this.MarkUnsaved();
                return;
            }

            base.Set(x, y, z, blockState);
        }
    }

    /// <summary>
    /// Sets the blocks of layer <paramref name="y"/>: entry <c>z * 16 + x</c> of <paramref name="layer"/> indexes
    /// <paramref name="blocks"/>, where null blocks are skipped. The result is the same as calling <see cref="Set"/> for each
    /// block in index order (palette order included), but much faster.
    /// </summary>
    internal void SetLayer(int y, ReadOnlySpan<byte> layer, ReadOnlySpan<IBlock?> blocks)
    {
        this.MarkUnsaved();

#if CACHE_VALID_BLOCKS
        validBlockCount.SetDirty();
#endif
        // Palette ids of the blocks once set, or -1. Indirect palettes only append, so ids stay valid.
        Span<int> ids = stackalloc int[blocks.Length];
        ids.Fill(-1);

        lock (this.dataLock)
        {
            var offset = y << 8;
            var data = this.DataArray;

            for (var i = 0; i < 256; i++)
            {
                var index = layer[i];
                var id = ids[index];
                if (id >= 0)
                {
                    data![offset + i] = id;
                    continue;
                }

                if (blocks[index] is not { } block)
                    continue;

                // The first of each block goes through the general path, which grows the palette and data array as needed.
                this.Set(i & 15, y, i >> 4, block);
                data = this.DataArray;
                if (data is not null && this.Palette is IndirectBlockPalette)
                    ids[index] = data[offset + i];
            }
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
                this.MarkUnsaved();
                return;
            }

            base.Set(x, y, z, BlocksRegistry.Get(stateId));
        }
    }

    public override void WriteTo(INetStreamWriter writer)
    {
#if CACHE_VALID_BLOCKS
        var (blocks, fluids) = validBlockCount.GetValue();
#else
        var (blocks, fluids) = CountBlocks();
#endif

        // Like vanilla's LevelChunkSection.write: the non-air blocks, then (since 26.1) those holding a fluid.
        writer.WriteShort(blocks);
        writer.WriteShort(fluids);

        if (this.BitsPerEntry > this.MaxBitsPerEntry && this.DataArray is DataArray data)
        {
            WriteGlobal(writer, data);
            return;
        }

        base.WriteTo(writer);
    }

    /// <summary>
    /// Writes the blocks as state ids without a palette, as vanilla's client reads a section of more than 8 bits:
    /// each takes as many bits as the registry's states need (16 for 26.3's 35,723). Obsidian keeps such a section's
    /// palette, which can grow past 8 bits.
    /// </summary>
    private void WriteGlobal(INetStreamWriter writer, DataArray data)
    {
        var bits = BlocksRegistry.GlobalBitsPerBlocks;
        var ids = new DataArray(bits, this.MaxEntryCount);
        var values = this.Palette is IndirectBlockPalette indirect ? indirect.Values : default;

        for (var index = 0; index < this.MaxEntryCount; index++)
            ids[index] = values.IsEmpty ? data[index] : values[data[index]];

        writer.WriteByte((byte)bits);
        writer.WriteLongArray(ids.storage);
    }

    /// <summary>
    /// Counts the section's blocks the way vanilla's section does: blocks that aren't air, and of those, the ones
    /// holding a fluid (water, lava, waterlogged blocks, kelp and the like).
    /// </summary>
    private (short Blocks, short Fluids) CountBlocks()
    {
        if (this.Palette is SingleValuePalette<IBlock> singleValuePalette)
        {
            var kind = KindOf(singleValuePalette.Value);
            var all = (short)this.MaxEntryCount;
            return ((kind & 1) != 0 ? all : (short)0, (kind & 2) != 0 ? all : (short)0);
        }

        var data = this.DataArray;
        if (data is null)
            return (0, 0);

        // Each palette entry's kind, looked up once; a global palette's entries are state ids.
        var palette = this.Palette;
        var global = palette is GlobalBlockStatePalette;
        Span<byte> kinds = global ? default : stackalloc byte[palette.Count];
        for (var id = 0; id < kinds.Length; id++)
            kinds[id] = palette.GetValueFromIndex(id) is IBlock block ? KindOf(block) : (byte)0;

        int blocks = 0, fluids = 0;
        for (int i = 0; i < this.MaxEntryCount; i++)
        {
            int index = data[i];
            var kind = global ? KindOf(BlocksRegistry.Get(index)) : kinds[index];
            blocks += kind & 1;
            fluids += kind >> 1;
        }

        return ((short)blocks, (short)fluids);
    }

    // Bit 0: the block isn't air. Bit 1: it also holds a fluid.
    private static byte KindOf(IBlock block) => block.IsAir ? (byte)0 : block.HasFluid() ? (byte)3 : (byte)1;

    public override BlockStateContainer Clone() => new(this.Palette.Clone(), this.IsSingleValued ? null : this.DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 4 | z) << 4 | x;
}
