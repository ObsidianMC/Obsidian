namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<Biome>
{
    public override IPalette<Biome> Palette { get; internal set; }

    internal override DataArray DataArray { get; private protected set; }

    internal BiomeContainer(byte bitsPerEntry = 0) : base(64, bitsPerEntry.DetermineBiomePalette(), Biome.Plains) { }

    private BiomeContainer(IPalette<Biome> palette, DataArray dataArray) : base(64, palette)
    {
        Palette = palette;
        DataArray = dataArray;
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
