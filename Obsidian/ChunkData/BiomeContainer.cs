using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : IDataContainer<Biomes>
{
    public byte BitsPerEntry { get; private set; }
    public IPalette<Biomes> Palette { get; private set; }

    public DataArray DataArray { get; set; }

    internal BiomeContainer(byte bitsPerEntry = 1)
    {
        this.BitsPerEntry = bitsPerEntry;

        this.Palette = bitsPerEntry.DetermineBiomePalette();

        this.DataArray = new(bitsPerEntry, 64);
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

    public int GetIndex(int x, int y, int z) => ((y >> 2) & 63) << 4 | ((z >> 2) & 3) << 2 | ((x >> 2) & 3);

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
