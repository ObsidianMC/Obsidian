using Obsidian.Net;


namespace Obsidian.ChunkData;
public class GlobalBiomePalette : IPalette<Biomes>
{
    public int[] Values => throw new NotImplementedException();

    public int Count => throw new NotImplementedException();

    public bool IsFull => false;

    public int GetIdFromValue(Biomes biome) => (int)biome;

    public Biomes GetValueFromIndex(int index) => (Biomes)index;

    public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;

    public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    public void WriteTo(MinecraftStream stream) { }
}