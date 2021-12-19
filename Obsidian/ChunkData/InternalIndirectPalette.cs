using Obsidian.Net;
using System.Runtime.CompilerServices;

namespace Obsidian.ChunkData;
internal sealed class InternalIndirectPalette<T> : IPalette<T> where T : Enum
{
    public int[] Values { get; }

    public int Size { get; set; }

    public bool IsFull => this.Values.Length == this.Size;

    public InternalIndirectPalette(byte bitCount) => this.Values = new int[1 << bitCount];

    public T? GetValueFromIndex(int index)
    {
        if ((uint)index >= (uint)this.Size)
            ThrowHelper.ThrowOutOfRange();

        return Unsafe.As<int, T>(ref this.Values[index]);
    }

    public int GetIdFromValue(T value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(value));

        var valueId = value.GetHashCode();

        ReadOnlySpan<int> valueIds = Values.AsSpan(0, Size);

        for (int id = 0; id < valueIds.Length; id++)
        {
            if (valueIds[id] == valueId)
                return id;
        }

        if (this.IsFull)
            return -1;

        var newId = this.Size;

        this.Values[newId] = valueId;

        this.Size++;

        return newId;
    }

    public async Task ReadFromAsync(MinecraftStream stream)
    {
        var length = await stream.ReadVarIntAsync();

        for (int i = 0; i < length; i++)
        {
            int id = await stream.ReadVarIntAsync();

            this.Values[i] = id;
            this.Size++;
        }
    }

    public async Task WriteToAsync(MinecraftStream stream)
    {
        await stream.WriteVarIntAsync(this.Size);

        for (int i = 0; i < this.Size; i++)
            await stream.WriteVarIntAsync(this.Values[i]);
    }

    public void WriteTo(MinecraftStream stream)
    {
        stream.WriteVarInt(this.Size);

        for (int i = 0; i < this.Size; i++)
            stream.WriteVarInt(this.Values[i]);
    }
}

