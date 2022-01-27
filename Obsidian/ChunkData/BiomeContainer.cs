using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<Biomes>
{
    public override IPalette<Biomes> Palette { get; internal set; }

    public override DataArray DataArray { get; protected set; }

    internal BiomeContainer(byte bitsPerEntry = 2) : base(bitsPerEntry)
    {
        Palette = bitsPerEntry.DetermineBiomePalette();
        DataArray = new(BitsPerEntry, 64);
    }

    private BiomeContainer(IPalette<Biomes> palette, DataArray dataArray, byte bitsPerEntry) : base(bitsPerEntry)
    {
        Palette = palette;
        DataArray = dataArray;
    }

    public void Set(int x, int y, int z, Biomes biome)
    {
        var index = GetIndex(x, y, z);

        var paletteIndex = Palette.GetOrAddId(biome);

        if (Palette.BitCount > DataArray.BitsPerEntry)
            DataArray = DataArray.Grow(Palette.BitCount);

        DataArray[index] = paletteIndex;
    }

    public Biomes Get(int x, int y, int z)
    {
        var storageId = DataArray[GetIndex(x, y, z)];

        return Palette.GetValueFromIndex(storageId);
    }

    public override async Task WriteToAsync(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(BitsPerEntry);

        await Palette.WriteToAsync(stream);

        stream.WriteVarInt(DataArray.storage.Length);

        long[] storage = DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }

    public override void WriteTo(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(BitsPerEntry);

        Palette.WriteTo(stream);

        stream.WriteVarInt(DataArray.storage.Length);

        long[] storage = DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }

    public BiomeContainer Clone()
    {
        return new BiomeContainer(Palette.Clone(), DataArray.Clone(), BitsPerEntry);
    }
}
