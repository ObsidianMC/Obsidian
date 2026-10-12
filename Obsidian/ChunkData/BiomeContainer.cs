using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<BiomeCodec>
{
    public override IPalette<BiomeCodec> Palette { get; internal set; }

    internal BiomeContainer(byte bitsPerEntry = 0) : base(bitsPerEntry, 1, 3, 64, ChunkData.PaletteFactory.DetermineBiomePalette) { }

    private BiomeContainer(IPalette<BiomeCodec> palette, DataArray? dataArray) : base(1, 3, 64, ChunkData.PaletteFactory.DetermineBiomePalette)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    /// <remarks>
    /// Reads don't lock, like block reads (see <see cref="BlockStateContainer.Get"/>).
    /// </remarks>
    public override BiomeCodec Get(int x, int y, int z)
    {
        var data = this.DataArray;
        var palette = this.Palette;

        if (data is not null)
        {
            if (palette is IndirectBiomePalette indirect && indirect.TryGetBiome(data[this.GetIndex(x, y, z)], out var biome))
                return biome;
        }
        else if (palette is SingleBiomeValuePalette single && single.IsFull)
        {
            return single.Value;
        }

        return base.Get(x, y, z);
    }

    /// <remarks>
    /// Like vanilla's client, which reads a biome section of more than 3 bits as biome ids, as many bits each as the
    /// biome registry needs, a section whose palette grew past 3 bits is written that way.
    /// </remarks>
    public override void WriteTo(INetStreamWriter writer)
    {
        if (this.Palette is not IndirectBiomePalette indirect
            || this.BitsPerEntry <= this.MaxBitsPerEntry
            || this.DataArray is not DataArray data)
        {
            base.WriteTo(writer);
            return;
        }

        var bits = CodecRegistry.Biomes.GlobalBitsPerEntry;
        var ids = new DataArray(bits, this.MaxEntryCount);
        var values = indirect.Values;

        for (var index = 0; index < this.MaxEntryCount; index++)
            ids[index] = values[data[index]];

        writer.WriteByte((byte)bits);
        writer.WriteLongArray(ids.storage);
    }

    public override BiomeContainer Clone() => new(this.Palette.Clone(), this.IsSingleValued ? null : this.DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
