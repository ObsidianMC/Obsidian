using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;
public interface IDataContainer<T>
{
    public byte BitsPerEntry { get; }

    public IPalette<T> Palette { get; }

    public DataArray DataArray { get; }

    public int GetIndex(int x, int y, int z);

    public Task WriteToAsync(MinecraftStream stream);
    public void WriteTo(MinecraftStream stream);
}

