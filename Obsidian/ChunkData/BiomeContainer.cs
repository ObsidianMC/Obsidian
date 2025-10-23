using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<BiomeCodec>
{
    public override IPalette<BiomeCodec> Palette { get; internal set; }

    internal override DataArray DataArray { get; private protected set; }

    internal BiomeContainer(byte bitsPerEntry = 0) : base(1, 3, 64, ChunkData.PaletteFactory.DetermineBiomePalette)
    {
        this.Palette = this.PaletteFactory(bitsPerEntry);

        if (!this.IsSingleValued)
            this.DataArray = new(this.MinBitsPerEntry, this.MaxEntryCount);
    }

    private BiomeContainer(IPalette<BiomeCodec> palette, DataArray dataArray) : base(1, 3, 64, ChunkData.PaletteFactory.DetermineBiomePalette)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    public override BiomeContainer Clone() => new(Palette.Clone(), DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
