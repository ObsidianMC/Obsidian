using System.Buffers;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A scratch array of <typeparamref name="T"/> borrowed from the current thread's <see cref="ScratchStack{T}"/>,
/// given back when disposed. Its contents start out unspecified.
/// </summary>
/// <remarks>
/// Rent it with <see cref="Rent"/> in a <c>using</c> declaration. Buffers are handed out and given back last in, first
/// out, which <c>using</c> scopes guarantee, during unwinding from an exception too. Being a ref struct, a buffer
/// can't be stored on the heap or carried to another thread.
/// </remarks>
internal readonly ref struct ScratchBuffer<T> where T : unmanaged
{
    // The stack that lent the array, or null for a default buffer or an array nothing owns (see ScratchStack.Rent).
    private readonly ScratchStack<T>? owner;
    private readonly T[] array;
    private readonly int length;
    private readonly int slot;
    private readonly long lease;

    internal ScratchBuffer(ScratchStack<T>? owner, T[] array, int length, int slot, long lease)
    {
        this.owner = owner;
        this.array = array;
        this.length = length;
        this.slot = slot;
        this.lease = lease;
    }

    public Span<T> Span => this.array.AsSpan(0, this.length);

    /// <summary>Borrows a buffer of <paramref name="length"/> elements from the current thread.</summary>
    public static ScratchBuffer<T> Rent(int length) => ScratchStack<T>.Current.Rent(length);

    public void Dispose() => this.owner?.Release(this.slot, this.lease);
}

/// <summary>
/// One thread's scratch arrays for density sampling: a stack of arrays that nested samples borrow and give back in
/// order (see <see cref="ScratchBuffer{T}"/>).
/// </summary>
/// <remarks>
/// <para>
/// A density tree borrows a few buffers at each node it descends through. <see cref="ArrayPool{T}.Shared"/> keeps one
/// array per size per thread, so nested rents of similar sizes fall through to its shared per-core lists; here each
/// depth keeps its own array, grown to the largest length borrowed at that depth, and a rent is a few field reads.
/// </para>
/// <para>
/// Generation threads live for the whole process, and so do their stacks. They hold only arrays of unmanaged
/// elements, never a density function, so they can't keep a plugin's objects alive. They're bounded: past
/// <see cref="MaxDepth"/> buffers, or once their arrays would hold more than <see cref="MaxRetainedElements"/>, a rent
/// falls back to <see cref="ArrayPool{T}.Shared"/>. An idle generation thread drops its arrays (see
/// <see cref="ScratchStacks.TrimCurrentThread"/>).
/// </para>
/// <para>
/// Every buffer gives its array back at most once, however often it or a copy of it is disposed: each rent gets a new
/// lease number, and only the lease the stack still records for the buffer gives its array back. A buffer disposed
/// while others rented after it are still out gives those back too, and their arrays are dropped from the stack
/// rather than handed out again, in case they're still in use.
/// </para>
/// </remarks>
internal sealed class ScratchStack<T> where T : unmanaged
{
    /// <summary>The most buffers a thread lends from its own arrays at once.</summary>
    /// <remarks>The overworld's density trees nest 18 deep.</remarks>
    public const int MaxDepth = 24;

    // The most shared-pool buffers a thread tracks at once. Further fallbacks get new arrays nothing returns.
    private const int MaxPooled = 8;

    // Array lengths are rounded up to this, so slightly longer volumes, such as the next chunk's ore vein selections,
    // usually fit the array already there.
    private const int GrowthStep = 1024;

    [ThreadStatic]
    private static ScratchStack<T>? current;

    private readonly T[]?[] arrays = new T[]?[MaxDepth];
    private readonly long[] leases = new long[MaxDepth];
    private readonly T[]?[] pooledArrays = new T[]?[MaxPooled];
    private readonly long[] pooledLeases = new long[MaxPooled];
    private int depth;
    // Leases count up from 1 and never wrap in practice, so 0 marks a free pooled entry and a stale lease never
    // matches a newer buffer's.
    private long nextLease;
    private int retained;

    /// <summary>The most elements a thread's stack keeps across all its arrays.</summary>
    /// <remarks>
    /// Sized from the overworld with about a third to spare: a thread keeps about 750,000 floats, a few material
    /// volumes of 16 by 16 by 384 blocks, and 190,000 ints for the ore veins' selections over them. The nether and the
    /// end keep a tenth of that. Taller dimensions fall back to the shared pool for their largest volumes.
    /// </remarks>
    public static int MaxRetainedElements { get; } = RetainedElements();

    /// <summary>The current thread's stack.</summary>
    public static ScratchStack<T> Current => current ??= new ScratchStack<T>();

    /// <summary>The current thread's stack, if it has made one.</summary>
    public static ScratchStack<T>? CurrentIfCreated => current;

    /// <summary>How many buffers are lent from the stack's own arrays.</summary>
    public int Depth => this.depth;

    /// <summary>Lends a buffer of <paramref name="length"/> elements on top of the stack.</summary>
    public ScratchBuffer<T> Rent(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (this.depth == MaxDepth || length > MaxRetainedElements)
            return this.RentPooled(length);

        var slot = this.depth;
        var array = this.arrays[slot];

        if (array is null || array.Length < length)
        {
            array = this.Grow(slot, length);

            if (array is null)
                return this.RentPooled(length);
        }

        this.nextLease++;
        this.leases[slot] = this.nextLease;
        this.depth++;

        return new ScratchBuffer<T>(this, array, length, slot, this.nextLease);
    }

    /// <summary>Drops the stack's arrays while it lends none of them, so an idle thread keeps no scratch.</summary>
    public void Trim()
    {
        if (this.depth != 0)
            return;

        Array.Clear(this.arrays);
        this.retained = 0;
    }

    /// <summary>
    /// Gives back the buffer lent at <paramref name="slot"/> and any lent after it, or, when <paramref name="slot"/>
    /// is negative, the shared-pool buffer lent under <paramref name="lease"/>.
    /// </summary>
    internal void Release(int slot, long lease)
    {
        if (slot < 0)
        {
            this.ReleasePooled(lease);
            return;
        }

        // Already given back, by this buffer or by one rented before it.
        if (slot >= this.depth || this.leases[slot] != lease)
            return;

        // Buffers rented after this one should have been given back first. They may still be in use, so they keep
        // their arrays and the stack forgets them.
        for (var above = slot + 1; above < this.depth; above++)
        {
            this.retained -= this.arrays[above]?.Length ?? 0;
            this.arrays[above] = null;
        }

        this.depth = slot;
    }

    // Replaces a slot's array with one of at least length elements, half again as long as the old one where that
    // fits, so a depth whose lengths creep up regrows rarely. Null when the stack would keep too many elements.
    private T[]? Grow(int slot, int length)
    {
        var old = this.arrays[slot]?.Length ?? 0;
        var others = this.retained - old;
        var needed = (length + GrowthStep - 1) / GrowthStep * GrowthStep;
        var size = Math.Max(needed, old / 2 * 3);

        if (others + size > MaxRetainedElements)
            size = needed;

        if (others + size > MaxRetainedElements)
            return null;

        var array = new T[size];
        this.arrays[slot] = array;
        this.retained = others + size;

        return array;
    }

    private static int RetainedElements()
    {
        if (typeof(T) == typeof(float))
            return 1 << 20;

        if (typeof(T) == typeof(int))
            return 1 << 18;

        return 1 << 16;
    }

    // Lends an array from the shared pool, recorded under its lease so that only one disposal returns it. Past
    // MaxPooled such buffers, the array is a new one that nothing returns.
    private ScratchBuffer<T> RentPooled(int length)
    {
        var entry = Array.IndexOf(this.pooledArrays, null);

        if (entry < 0)
            return new ScratchBuffer<T>(null, new T[length], length, slot: -1, lease: 0);

        var array = ArrayPool<T>.Shared.Rent(length);
        this.nextLease++;
        this.pooledArrays[entry] = array;
        this.pooledLeases[entry] = this.nextLease;

        return new ScratchBuffer<T>(this, array, length, slot: -1, this.nextLease);
    }

    private void ReleasePooled(long lease)
    {
        var entry = Array.IndexOf(this.pooledLeases, lease);

        // Already returned, by this buffer or a copy of it.
        if (entry < 0 || this.pooledArrays[entry] is not T[] array)
            return;

        this.pooledArrays[entry] = null;
        this.pooledLeases[entry] = 0;
        ArrayPool<T>.Shared.Return(array);
    }
}

/// <summary>The scratch stacks density sampling uses (see <see cref="ScratchStack{T}"/>).</summary>
internal static class ScratchStacks
{
    /// <summary>
    /// Drops the current thread's scratch arrays, which it regrows when it next samples. Generation threads call it
    /// when they run out of work.
    /// </summary>
    public static void TrimCurrentThread()
    {
        ScratchStack<float>.CurrentIfCreated?.Trim();
        ScratchStack<int>.CurrentIfCreated?.Trim();
        ScratchStack<byte>.CurrentIfCreated?.Trim();
    }
}
