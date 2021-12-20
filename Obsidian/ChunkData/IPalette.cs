using Obsidian.Net;

namespace Obsidian.ChunkData;

public interface IPalette<T>
{
    public int[] Values { get; }
    public int Count { get; }
    public bool IsFull { get; }

    public int GetIdFromValue(T value);
    public T? GetValueFromIndex(int index);

    public void WriteTo(MinecraftStream stream);
    public Task WriteToAsync(MinecraftStream stream);
    public Task ReadFromAsync(MinecraftStream stream);
}
