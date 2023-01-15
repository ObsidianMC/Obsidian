﻿using Obsidian.Net;

namespace Obsidian.ChunkData;

public class GlobalBiomePalette : IPalette<Biome>
{
    public int[] Values => throw new NotSupportedException();
    public int BitCount { get; }
    public int Count => throw new NotSupportedException();
    public bool IsFull => false;

    public GlobalBiomePalette(int bitCount)
    {
        this.BitCount = bitCount;
    }

    public bool TryGetId(Biome biome, out int id)
    {
        id = (int)biome;
        return true;
    }

    public int GetOrAddId(Biome biome) => (int)biome;

    public Biome GetValueFromIndex(int index) => (Biome)index;

    public IPalette<Biome> Clone() => this;

    public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;
    public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    public void WriteTo(MinecraftStream stream)
    {
    }
}
