using Obsidian.Net;

namespace Obsidian.ChunkData;
public class IndirectBiomePalette : IBiomePalette
{
    public int[] Biomes { get; }

    public int BiomeCount { get; private set; }

    public bool IsFull => this.Biomes.Length == this.BiomeCount;

    public IndirectBiomePalette(byte bitCount) => this.Biomes = new int[1 << bitCount];

    public Biomes GetBiomeFromIndex(int index)
    {
        if (index > this.BiomeCount - 1 || index < 0)
            throw new IndexOutOfRangeException();

        return (Biomes)this.Biomes[index];
    }

    public int GetIdFromBiome(Biomes biome)
    {
        foreach (var id in this.Biomes)
        {
            if (id == (int)biome)
                return id;
        }

        if (this.IsFull)
            return -1;

        var newId = this.BiomeCount++;

        this.Biomes[newId] = (int)biome;

        return newId;
    }

    public async Task ReadFromAsync(MinecraftStream stream)
    {
        var length = await stream.ReadVarIntAsync();

        for (int i = 0; i < length; i++)
        {
            int id = await stream.ReadVarIntAsync();

            this.Biomes[i] = id;
            this.BiomeCount++;
        }
    }

    public async Task WriteToAsync(MinecraftStream stream)
    {
        await stream.WriteVarIntAsync(this.BiomeCount);

        for (int i = 0; i < this.BiomeCount; i++)
            await stream.WriteVarIntAsync(this.Biomes[i]);
    }

    public void WriteTo(MinecraftStream stream)
    {
        stream.WriteVarInt(this.BiomeCount);

        for (int i = 0; i < this.BiomeCount; i++)
            stream.WriteVarInt(this.Biomes[i]);
    }
}

