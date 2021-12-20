using Obsidian.Net;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace Obsidian.ChunkData;

public abstract class BaseIndirectPalette<T> : IPalette<T>
{
    private delegate T Factory(int value);

    public int[] Values { get; }
    public int Count { get; protected set; }
    public bool IsFull => Count == Values.Length;

    public BaseIndirectPalette(byte bitCount)
    {
        Values = new int[1 << bitCount];
    }

    public abstract T? GetValueFromIndex(int index);

    public int GetIdFromValue(T value)
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
        }

        int valueId = value!.GetHashCode();

        ReadOnlySpan<int> valueIds = GetSpan();
        for (int id = 0; id < valueIds.Length; id++)
        {
            if (valueIds[id] == valueId)
                return id;
        }

        if (IsFull)
            return -1;

        var newId = Count;
        Values[Count++] = valueId;
        return newId;
    }

    public async Task ReadFromAsync(MinecraftStream stream)
    {
        var length = await stream.ReadVarIntAsync();

        for (int i = 0; i < length; i++)
        {
            int id = await stream.ReadVarIntAsync();

            Values[i] = id;
            Count++;
        }
    }

    public async Task WriteToAsync(MinecraftStream stream)
    {
        await stream.WriteVarIntAsync(Count);

        for (int i = 0; i < Count; i++)
            await stream.WriteVarIntAsync(Values[i]);
    }

    public void WriteTo(MinecraftStream stream)
    {
        stream.WriteVarInt(Count);

        ReadOnlySpan<int> values = GetSpan();
        for (int i = 0; i < values.Length; i++)
            stream.WriteVarInt(values[i]);
    }

    protected ReadOnlySpan<int> GetSpan()
    {
        ref int first = ref MemoryMarshal.GetArrayDataReference(Values);
        return MemoryMarshal.CreateReadOnlySpan(ref first, Count);
    }
}
