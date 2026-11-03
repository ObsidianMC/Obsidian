using System.Buffers;
using System.Runtime.CompilerServices;

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
    // The stack that lent the array, or null when it came from ArrayPool<T>.Shared (see ScratchStack.Rent).
    private readonly ScratchStack<T>? owner;
    private readonly T[] array;
    private readonly int length;
    private readonly int slot;
    private readonly int lease;

    internal ScratchBuffer(ScratchStack<T>? owner, T[] array, int length, int slot, int lease)
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

    public void Dispose()
    {
        // A default buffer has nothing to give back.
        if (this.array is null)
            return;

        if (this.owner is null)
        {
            ArrayPool<T>.Shared.Return(this.array);
            return;
        }

        this.owner.Release(this.slot, this.lease);
    }
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
/// <see cref="MaxDepth"/> buffers, or once their arrays would hold more than <see cref="MaxRetainedBytes"/>, a rent
/// falls back to <see cref="ArrayPool{T}.Shared"/>.
/// </para>
/// <para>
/// Each rent gets a new lease number, and a buffer gives its array back only while its slot still holds its lease, so
/// disposing a copy twice or in the wrong order can't free another buffer's array. A buffer disposed while others
/// rented after it are still out gives those back too, and their arrays are dropped from the stack rather than
/// handed out again, in case they're still in use.
/// </para>
/// </remarks>
internal sealed class ScratchStack<T> where T : unmanaged
{
    /// <summary>The most buffers a thread lends at once before falling back to the shared pool.</summary>
    /// <remarks>The overworld's density trees nest about 18 deep.</remarks>
    public const int MaxDepth = 32;

    /// <summary>The most bytes a thread's stack keeps across all its arrays.</summary>
    /// <remarks>
    /// The overworld keeps about 3 MB of floats and 1 MB of ints per thread, a few material volumes of 16 by 16 by 384
    /// blocks and the ore veins' selections over them; taller dimensions keep more.
    /// </remarks>
    public const int MaxRetainedBytes = 8 << 20;

    // Array lengths are rounded up to this, so slightly longer volumes, such as the next chunk's ore vein selections,
    // usually fit the array already there.
    private const int GrowthStep = 1024;

    private static readonly int maxRetained = MaxRetainedBytes / Unsafe.SizeOf<T>();

    [ThreadStatic]
    private static ScratchStack<T>? current;

    private readonly T[]?[] arrays = new T[]?[MaxDepth];
    private readonly int[] leases = new int[MaxDepth];
    private int depth;
    private int nextLease;
    private int retained;

    /// <summary>The current thread's stack.</summary>
    public static ScratchStack<T> Current => current ??= new ScratchStack<T>();

    /// <summary>How many buffers are lent out.</summary>
    public int Depth => this.depth;

    /// <summary>How many elements the stack's arrays hold.</summary>
    public int Retained => this.retained;

    /// <summary>Lends a buffer of <paramref name="length"/> elements on top of the stack.</summary>
    public ScratchBuffer<T> Rent(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (this.depth == MaxDepth || length > maxRetained)
            return Pooled(length);

        var slot = this.depth;
        var array = this.arrays[slot];

        if (array is null || array.Length < length)
        {
            array = this.Grow(slot, length);

            if (array is null)
                return Pooled(length);
        }

        this.nextLease++;
        this.leases[slot] = this.nextLease;
        this.depth++;

        return new ScratchBuffer<T>(this, array, length, slot, this.nextLease);
    }

    /// <summary>Gives back the buffer lent at <paramref name="slot"/>, and any lent after it.</summary>
    internal void Release(int slot, int lease)
    {
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

        if (others + size > maxRetained)
            size = needed;

        if (others + size > maxRetained)
            return null;

        var array = new T[size];
        this.arrays[slot] = array;
        this.retained = others + size;

        return array;
    }

    private static ScratchBuffer<T> Pooled(int length) =>
        new(null, ArrayPool<T>.Shared.Rent(length), length, slot: -1, lease: 0);
}
