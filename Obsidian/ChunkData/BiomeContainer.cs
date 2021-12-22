using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : IDataContainer<Biomes>
{
    public byte BitsPerEntry { get; private set; }
    public IPalette<Biomes> Palette { get; private set; }

    public DataArray DataArray { get; set; }

    internal BiomeContainer(byte bitsPerEntry = 2)
    {
        this.BitsPerEntry = (byte)(bitsPerEntry > 3 ? 6 : bitsPerEntry);

        this.Palette = bitsPerEntry.DetermineBiomePalette();

        this.DataArray = new(this.BitsPerEntry, 64);
    }

    public bool Set(int x, int y, int z, Biomes biome)
    {
        var index = this.GetIndex(x, y, z);

        var paletteIndex = this.Palette.GetOrAddId(biome);
        if (paletteIndex == -1)
            return false;

        this.DataArray[index] = paletteIndex;
        return true;
    }

    public Biomes Get(int x, int y, int z)
    {
        var storageId = this.DataArray[this.GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    public async Task WriteToAsync(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(this.BitsPerEntry);

        await this.Palette.WriteToAsync(stream);

        stream.WriteVarInt(this.DataArray.storage.Length);

        long[] storage = this.DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }

    public void WriteTo(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(this.BitsPerEntry);

        this.Palette.WriteTo(stream);

        stream.WriteVarInt(this.DataArray.storage.Length);

        long[] storage = this.DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }
}
