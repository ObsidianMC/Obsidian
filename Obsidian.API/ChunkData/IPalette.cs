namespace Obsidian.API.ChunkData;
public interface IPalette<T>
{
    public int[] Values { get; }
    public int Count { get; }
    public int BitCount { get; }
    public bool IsFull { get; }

    public bool TryGetId(T value, out int id);
    public int GetOrAddId(T value);
    public T? GetValueFromIndex(int index);

    public IPalette<T> Clone();

    public void WriteTo(INetStreamWriter writer);
}
