using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// The cell corner values of recently sampled columns on chunk borders, which the chunks on both sides of a border share.
/// </summary>
/// <remarks>
/// A corner's value only depends on the interpolated function and the corner: corners lie inside the flat caches of every
/// chunk that samples them, so neighbors compute the same values. Each function keeps one column per slot of a 32 by 32
/// column (8 by 8 chunk) window; a column a window away takes the slot over. Thread-safe without allocating: each slot is
/// guarded by a sequence lock, a version that's odd while the slot is written, so readers notice when a write overlapped
/// their read and treat it as a miss.
/// </remarks>
internal sealed class CornerColumnCache
{
    private const int Window = 32;
    private const int FunctionSlots = 8;
    private const int SlotCount = FunctionSlots * Window * Window;

    private readonly int[] versions = new int[SlotCount];
    private readonly object?[] functions = new object?[SlotCount];
    private readonly int[] cellXs = new int[SlotCount];
    private readonly int[] cellZs = new int[SlotCount];

    // The slots' corners, one array per function slot, allocated on first use.
    private readonly double[]?[] values = new double[]?[FunctionSlots];
    private readonly int columnLength;
    private readonly ConcurrentDictionary<object, int> functionSlots = new(ReferenceEqualityComparer.Instance);

    /// <param name="columnLength">The number of corners in a column.</param>
    public CornerColumnCache(int columnLength) => this.columnLength = columnLength;

    /// <summary>
    /// Copies the corners of column (<paramref name="cellX"/>, <paramref name="cellZ"/>) of an interpolated function into
    /// <paramref name="values"/>, if they're kept. Otherwise <paramref name="values"/> may have been written to.
    /// </summary>
    /// <param name="function">The router's interpolated function.</param>
    public bool TryGet(object function, int cellX, int cellZ, Span<double> values)
    {
        var functionSlot = this.FunctionSlot(function);
        var data = Volatile.Read(ref this.values[functionSlot]);
        if (data is null || values.Length != this.columnLength)
            return false;

        var slot = Slot(functionSlot, cellX, cellZ);
        ref var version = ref this.versions[slot];
        var before = Volatile.Read(ref version);
        if ((before & 1) != 0 || this.functions[slot] != function || this.cellXs[slot] != cellX || this.cellZs[slot] != cellZ)
            return false;

        data.AsSpan(slot % (Window * Window) * this.columnLength, this.columnLength).CopyTo(values);

        // The reads above complete before the version is read again.
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref version) == before;
    }

    /// <summary>
    /// Keeps the corners of a column of an interpolated function, unless another thread is writing the slot.
    /// </summary>
    public void Add(object function, int cellX, int cellZ, ReadOnlySpan<double> values)
    {
        if (values.Length != this.columnLength)
            return;

        var functionSlot = this.FunctionSlot(function);
        var data = Volatile.Read(ref this.values[functionSlot]);
        if (data is null)
        {
            Interlocked.CompareExchange(ref this.values[functionSlot], new double[Window * Window * this.columnLength], null);
            data = Volatile.Read(ref this.values[functionSlot])!;
        }

        var slot = Slot(functionSlot, cellX, cellZ);
        ref var version = ref this.versions[slot];
        var current = Volatile.Read(ref version);
        if ((current & 1) != 0 || Interlocked.CompareExchange(ref version, current + 1, current) != current)
            return;

        this.functions[slot] = function;
        this.cellXs[slot] = cellX;
        this.cellZs[slot] = cellZ;
        values.CopyTo(data.AsSpan(slot % (Window * Window) * this.columnLength, this.columnLength));

        Volatile.Write(ref version, current + 2);
    }

    // Functions share slots past the 8th, which only costs hits.
    private int FunctionSlot(object function) =>
        this.functionSlots.GetOrAdd(function, static (_, slots) => slots.Count, this.functionSlots) % FunctionSlots;

    private static int Slot(int functionSlot, int cellX, int cellZ) =>
        (functionSlot * Window + (cellX & (Window - 1))) * Window + (cellZ & (Window - 1));
}
