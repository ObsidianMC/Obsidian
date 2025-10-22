using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<BiomeCodec>
{
    public override IPalette<BiomeCodec> Palette { get; internal set; }

    internal override DataArray DataArray { get; private protected set; }

    internal BiomeContainer(byte bitsPerEntry = 0) : base(1, 3, 64, ChunkData.PaletteFactory.DetermineBiomePalette)
    {
        this.Palette = this.PaletteFactory(bitsPerEntry);
    }

    private BiomeContainer(IPalette<BiomeCodec> palette, DataArray dataArray) : base(1, 3, 64, ChunkData.PaletteFactory.DetermineBiomePalette)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    public override void Set(int x, int y, int z, BiomeCodec biome)
    {
        var index = this.GetIndex(x, y, z);

        var paletteIndex = this.Palette.GetOrAddId(biome);

        if (this.TryGrow(paletteIndex))
            paletteIndex = this.Palette.GetOrAddId(biome);

        if (!this.IsSingleValued)
            this.DataArray[index] = paletteIndex;
    }

    public override BiomeCodec Get(int x, int y, int z)
    {
        if (this.Palette is SingleValuePalette<BiomeCodec> singleValuePalette)
            return singleValuePalette.Value;

        var storageId = this.DataArray[this.GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public override BiomeContainer Clone() => new(Palette.Clone(), DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
