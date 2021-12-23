﻿using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;

public sealed class BiomeContainer : DataContainer<Biomes>
{
    public override IPalette<Biomes> Palette { get; internal set; }

    public override DataArray DataArray { get; protected set; }

    internal BiomeContainer(byte bitsPerEntry = 2) : base((byte)(bitsPerEntry > 3 ? 6 : bitsPerEntry))
    {
        this.Palette = bitsPerEntry.DetermineBiomePalette();

        this.DataArray = new(this.BitsPerEntry, 64);
    }

    public void Set(int x, int y, int z, Biomes biome)
    {
        var index = this.GetIndex(x, y, z);

        var paletteIndex = this.Palette.GetOrAddId(biome);

        this.DataArray[index] = paletteIndex;
    }

    public Biomes Get(int x, int y, int z)
    {
        var storageId = this.DataArray[this.GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public override async Task WriteToAsync(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(this.BitsPerEntry);

        await this.Palette.WriteToAsync(stream);

        stream.WriteVarInt(this.DataArray.storage.Length);

        long[] storage = this.DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }

    public override void WriteTo(MinecraftStream stream)
    {
        stream.WriteUnsignedByte(this.BitsPerEntry);

        this.Palette.WriteTo(stream);

        stream.WriteVarInt(this.DataArray.storage.Length);

        long[] storage = this.DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }
}
