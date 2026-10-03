using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Exceptions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.ChunkData;

public sealed class IndirectBlockPalette : BaseIndirectPalette<IBlock>, IPalette<IBlock>
{
    public IndirectBlockPalette(byte bitCount) : base(bitCount)
    {
    }

    private IndirectBlockPalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override IBlock? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new MissingPaletteEntryException(index);

        var block = BlocksRegistry.Get(Values[index]);

        return block is null ? throw new MissingPaletteEntryException(index) : block;
    }

    /// <summary>
    /// The block at a palette index, for reads that don't lock: <c>false</c> when the entry isn't readable yet.
    /// </summary>
    /// <remarks>
    /// The values array is read before the count. A grown array replaces a full one, so an index past the array read is
    /// rejected, and an index under the count read was written before it.
    /// </remarks>
    public bool TryGetBlock(int index, [NotNullWhen(true)] out IBlock? block)
    {
        var values = this.Values;
        if ((uint)index < (uint)this.Count && (uint)index < (uint)values.Length)
        {
            block = BlocksRegistry.Get(values[index]);
            return true;
        }

        block = null;
        return false;
    }

    /// <summary>
    /// The state id at a palette index, for reads that don't lock; see <see cref="TryGetBlock"/>.
    /// </summary>
    public bool TryGetStateId(int index, out int stateId)
    {
        var values = this.Values;
        if ((uint)index < (uint)this.Count && (uint)index < (uint)values.Length)
        {
            stateId = values[index];
            return true;
        }

        stateId = 0;
        return false;
    }

    public override IPalette<IBlock> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Values[..Count].CopyTo(valuesCopy);
        return new IndirectBlockPalette(valuesCopy, BitCount, Count);
    }

    protected override int GetValueId(IBlock value) => value.GetHashCode();
}


public sealed class IndirectBiomePalette : BaseIndirectPalette<BiomeCodec>, IPalette<BiomeCodec>
{
    public IndirectBiomePalette(byte bitCount) : base(bitCount)
    {
    }

    private IndirectBiomePalette(int[] values, int bitCount, int count) : base(values, bitCount, count)
    {
    }

    public override BiomeCodec? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new MissingPaletteEntryException(index);

        var biome = CodecRegistry.GetBiome(Values[index]);

        return biome is null ? throw new MissingPaletteEntryException(index) : biome;
    }

    /// <summary>
    /// The biome at a palette index, for reads that don't lock; see <see cref="IndirectBlockPalette.TryGetBlock"/>.
    /// </summary>
    public bool TryGetBiome(int index, [NotNullWhen(true)] out BiomeCodec? biome)
    {
        var values = this.Values;
        if ((uint)index < (uint)this.Count && (uint)index < (uint)values.Length)
        {
            biome = CodecRegistry.GetBiome(values[index]);
            return biome is not null;
        }

        biome = null;
        return false;
    }

    public override IPalette<BiomeCodec> Clone()
    {
        int[] valuesCopy = GC.AllocateUninitializedArray<int>(Values.Length);
        Values[..Count].CopyTo(valuesCopy);
        return new IndirectBiomePalette(valuesCopy, BitCount, Count);
    }

    protected override int GetValueId(BiomeCodec value) => value.Id;
}
