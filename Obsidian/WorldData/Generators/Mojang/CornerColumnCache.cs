using System.Threading;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// The cell corner values of recently sampled columns on chunk borders, which the chunks on both sides of a border share.
/// </summary>
/// <remarks>
/// A corner's value only depends on the interpolated function and the corner: corners lie inside the flat caches of every
/// chunk that samples them, so neighbors compute the same values. Each function keeps one column per slot of a 32 by 32
/// column (8 by 8 chunk) window; a column a window away takes the slot over. Thread-safe, as entries don't change once
/// added.
/// </remarks>
internal sealed class CornerColumnCache
{
    private const int Window = 32;
    private const int FunctionSlots = 8;

    private readonly Entry?[] entries = new Entry?[FunctionSlots * Window * Window];
    private readonly ConcurrentDictionary<object, int> functionSlots = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Copies the corners of column (<paramref name="cellX"/>, <paramref name="cellZ"/>) of an interpolated function into
    /// <paramref name="values"/>, if they're kept.
    /// </summary>
    /// <param name="function">The router's interpolated function.</param>
    public bool TryGet(object function, int cellX, int cellZ, Span<double> values)
    {
        var entry = Volatile.Read(ref this.entries[this.Slot(function, cellX, cellZ)]);
        if (entry is null || entry.Function != function || entry.CellX != cellX || entry.CellZ != cellZ || entry.Values.Length != values.Length)
            return false;

        entry.Values.CopyTo(values);
        return true;
    }

    /// <summary>
    /// Keeps the corners of a column of an interpolated function.
    /// </summary>
    public void Add(object function, int cellX, int cellZ, ReadOnlySpan<double> values) =>
        Volatile.Write(ref this.entries[this.Slot(function, cellX, cellZ)], new Entry(function, cellX, cellZ, values.ToArray()));

    // Functions share slots past the 8th, which only costs hits.
    private int Slot(object function, int cellX, int cellZ)
    {
        var functionSlot = this.functionSlots.GetOrAdd(function, static (_, slots) => slots.Count, this.functionSlots) % FunctionSlots;
        return (functionSlot * Window + (cellX & (Window - 1))) * Window + (cellZ & (Window - 1));
    }

    private sealed record Entry(object Function, int CellX, int CellZ, double[] Values);
}
