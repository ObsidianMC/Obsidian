using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;
public interface IDataContainer
{
    public byte BitsPerEntry { get; }

    public DataArray DataArray { get; }

    public int GetIndex(int x, int y, int z);

    public Task WriteToAsync(MinecraftStream stream);
    public void WriteTo(MinecraftStream stream);
}

