using System.Runtime.InteropServices;
using System.Threading;

namespace Obsidian.ChunkData;

public abstract class BaseIndirectPalette<T> : IPalette<T>
{
    public int[] Values { get; private set; }
    public int BitCount { get; private set; }
    // Reads of the block storage don't lock (see BlockStateContainer.Get): an entry is written before the count that
    // makes it readable.
    private int count;
    public int Count { get => Volatile.Read(ref this.count); protected set => Volatile.Write(ref this.count, value); }
    public bool IsFull => Count == Values.Length;

    public bool ShouldGrow => false;

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
        // A null check rather than typeof(T).IsValueType, which shared generic code can't fold away.
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        int valueId = this.GetValueId(value);

        return TryGetIdImpl(valueId, out id);
    }

    private bool TryGetIdImpl(int valueId, out int id)
    {
        id = GetSpan().IndexOf(valueId);
        return id >= 0;
    }

    public int GetOrAddId(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        // Get
        int valueId = this.GetValueId(value);
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

    protected abstract int GetValueId(T value);
}
