using Obsidian.Net;
using Obsidian.Utilities.Collections;

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

    public void Set(int x, int y, int z, Biome biome)
    {
        var index = this.GetIndex(x, y, z);

        var paletteIndex = this.Palette.GetOrAddId(biome);

        if (Palette.BitCount > DataArray.BitsPerEntry)
            DataArray = DataArray.Grow(Palette.BitCount);

        this.DataArray[index] = paletteIndex;
    }

    public Biome Get(int x, int y, int z)
    {
        var storageId = this.DataArray[this.GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public async override Task WriteToAsync(MinecraftStream stream)
    {
        await stream.WriteUnsignedByteAsync(this.BitsPerEntry);

        await this.Palette.WriteToAsync(stream);

        await stream.WriteVarIntAsync(this.DataArray.storage.Length);

        long[] storage = this.DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            await stream.WriteLongAsync(storage[i]);
    }

    public override void WriteTo(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(this.BitsPerEntry);

        this.Palette.WriteTo(stream);

        stream.WriteVarInt(this.DataArray.storage.Length);
        stream.WriteLongArray(this.DataArray.storage);
    }

    public BiomeContainer Clone()
    {
        return new BiomeContainer(Palette.Clone(), DataArray.Clone());
    }

    public override int GetIndex(int x, int y, int z) => (y << 2 | z) << 2 | x;
}
