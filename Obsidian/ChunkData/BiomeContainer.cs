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

    public override BiomeContainer Clone() => new(this.Palette.Clone(), this.IsSingleValued ? null : this.DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
