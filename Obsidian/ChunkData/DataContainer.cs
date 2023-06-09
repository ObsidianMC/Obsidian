using Obsidian.Net;
using Obsidian.Utilities.Collections;

namespace Obsidian.ChunkData;
public abstract class DataContainer<T>
{
    public byte BitsPerEntry => (byte)DataArray.BitsPerEntry;

    public abstract IPalette<T> Palette { get; internal set; }

    internal abstract DataArray DataArray { get; private protected set; }

    public virtual int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    public abstract Task WriteToAsync(MinecraftStream stream);
    public abstract void WriteTo(MinecraftStream stream);
}
