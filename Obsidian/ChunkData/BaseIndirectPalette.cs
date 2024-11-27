using Obsidian.Net;
using System.Runtime.InteropServices;

namespace Obsidian.ChunkData;

public abstract class BaseIndirectPalette<T> : IPalette<T>
{
    public int[] Values { get; private set; }
    public int BitCount { get; private set; }
    public int Count { get; protected set; }
    public bool IsFull => Count == Values.Length;
    public BaseIndirectPalette(byte bitCount)
    {
        BitCount = bitCount;
        Values = GC.AllocateUninitializedArray<int>(1 << bitCount);
    }

    protected BaseIndirectPalette(int[] values, int bitCount, int count)
    {
        Values = values;
        BitCount = bitCount;
        Count = count;
    }

    public abstract T? GetValueFromIndex(int index);

    public bool TryGetId(T value, out int id)
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
        }

        int valueId = value!.GetHashCode();
        return TryGetIdImpl(valueId, out id);
    }

    private bool TryGetIdImpl(int valueId, out int id)
    {
        ReadOnlySpan<int> valueIds = GetSpan();
        for (id = 0; id < valueIds.Length; id++)
        {
            if (valueIds[id] == valueId)
            {
                return true;
            }
        }

        return false;
    }

    public int GetOrAddId(T value)
    {
        if (!typeof(T).IsValueType)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
        }

        // Get
        int valueId = value!.GetHashCode();
        if (TryGetIdImpl(valueId, out int id))
            return id;

        // Add
        if (IsFull)
        {
            BitCount++;
            int[] newArray = GC.AllocateUninitializedArray<int>(1 << BitCount);
            Array.Copy(Values, newArray, Values.Length);
            Values = newArray;
        }

        var newId = Count++;
        Values[newId] = valueId;
        return newId;
    }

    public virtual IPalette<T> Clone()
    {
        throw new NotSupportedException();
    }

    public void WriteTo(INetStreamWriter writer)
    {
        writer.WriteVarInt(Count);

        ReadOnlySpan<int> values = GetSpan();

        for (int i = 0; i < values.Length; ++i)
            writer.WriteVarInt(values[i]);
    }

    protected ReadOnlySpan<int> GetSpan()
    {
        ref int first = ref MemoryMarshal.GetArrayDataReference(Values);
        return MemoryMarshal.CreateReadOnlySpan(ref first, Count);
    }
}
