namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<Biome>
{
    public override IPalette<Biome> Palette { get; internal set; }

    internal override DataArray DataArray { get; private protected set; }

    internal BiomeContainer(byte bitsPerEntry = 2)
    {
        this.Palette = bitsPerEntry.DetermineBiomePalette();
        this.DataArray = new(bitsPerEntry, 64);
    }

    private BiomeContainer(IPalette<Biome> palette, DataArray dataArray)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    public override void Set(int x, int y, int z, Biome biome)
    {
        var index = this.GetIndex(x, y, z);

        var paletteIndex = this.Palette.GetOrAddId(biome);

        this.GrowDataArray();

        this.DataArray[index] = paletteIndex;
    }

    public override Biome Get(int x, int y, int z)
    {
        var storageId = this.DataArray[this.GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public override void WriteTo(INetStreamWriter writer)
    {
        writer.WriteByte(this.BitsPerEntry);

        this.Palette.WriteTo(writer);

        writer.WriteVarInt(this.DataArray.storage.Length);
        writer.WriteLongArray(this.DataArray.storage);
    }

    public override BiomeContainer Clone() => new(Palette.Clone(), DataArray.Clone());

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
