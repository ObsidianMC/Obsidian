using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<BiomeCodec>
{
    public override IPalette<BiomeCodec> Palette { get; internal set; }

    internal override DataArray DataArray { get; private protected set; }

    internal BiomeContainer(byte bitsPerEntry = 2)
    {
        this.Palette = bitsPerEntry.DetermineBiomePalette();
        this.DataArray = new(bitsPerEntry, 64);
    }

    private BiomeContainer(IPalette<BiomeCodec> palette, DataArray dataArray)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    public override void Set(int x, int y, int z, BiomeCodec biome)
    {
        var index = this.GetIndex(x, y, z);

        var paletteIndex = this.Palette.GetOrAddId(biome);

        this.GrowDataArray();

        this.DataArray[index] = paletteIndex;
    }

    public override BiomeCodec Get(int x, int y, int z)
    {
        var storageId = this.DataArray[this.GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public override void WriteTo(INetStreamWriter writer)
    {
        writer.WriteByte(this.BitsPerEntry);

        this.Palette.WriteTo(writer);

        writer.WriteLongArray(this.DataArray.storage);
    }

    public override BiomeContainer Clone() => new(Palette.Clone(), DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
