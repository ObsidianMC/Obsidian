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

    public override BiomeContainer Clone() => new(this.Palette.Clone(), this.IsSingleValued ? null : this.DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
