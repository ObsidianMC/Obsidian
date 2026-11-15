using Obsidian.API.Utilities;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.API.ChunkData;

/// <summary>
/// Stores section values through a palette and optional packed palette indices.
/// </summary>
public abstract class DataContainer<T>(byte minBitsPerEntry, byte maxBitsPerEntry, int maxEntryCount, Func<byte, IPalette<T>> paletteFactory)
{
    private protected readonly Lock dataLock = new();
    /// <summary>
    /// Gets whether this container reports itself empty.
    /// </summary>
    public virtual bool IsEmpty { get; }

    /// <summary>
    /// Gets the current palette index width in bits.
    /// </summary>
    public byte BitsPerEntry => (byte)this.Palette.BitCount;

    /// <summary>
    /// Gets the initial packed index width used after leaving a single-value palette.
    /// </summary>
    public byte MinBitsPerEntry { get; } = minBitsPerEntry;
    /// <summary>
    /// Gets the configured maximum local palette index width.
    /// </summary>
    public byte MaxBitsPerEntry { get; } = maxBitsPerEntry;
    /// <summary>
    /// Gets the number of positions represented by the packed storage.
    /// </summary>
    public int MaxEntryCount { get; } = maxEntryCount;

    /// <summary>
    /// Gets the factory selecting a palette for a requested bit width.
    /// </summary>
    public Func<byte, IPalette<T>> PaletteFactory { get; } = paletteFactory;

    /// <summary>
    /// Gets whether the container uses a zero-bit <see cref="SingleValuePalette{T}"/>.
    /// </summary>
    [MemberNotNullWhen(false, nameof(DataArray))]
    public bool IsSingleValued => this.Palette is SingleValuePalette<T>;

    /// <summary>
    /// Gets the palette mapping stored indices to values.
    /// </summary>
    public abstract IPalette<T> Palette { get; internal set; }

    internal DataArray? DataArray { get; private protected set; }

    // 1 while the container changed since its chunk's last save took the mark (see TakeUnsaved). A new one is unsaved.
    private int unsaved = 1;

    /// <summary>
    /// Whether the container changed since <see cref="TakeUnsaved"/> last cleared the mark: every write marks it.
    /// </summary>
    internal bool IsUnsaved => Volatile.Read(ref this.unsaved) != 0;

    /// <summary>
    /// Records a write, which its chunk's next save has to write. Subclasses' own writes call it.
    /// </summary>
    internal void MarkUnsaved() => Volatile.Write(ref this.unsaved, 1);

    /// <summary>
    /// Clears the mark, for a save about to snapshot the container.
    /// </summary>
    /// <returns>Whether it was marked. A write after this marks it again, so the next save has it.</returns>
    internal bool TakeUnsaved() => Interlocked.Exchange(ref this.unsaved, 0) != 0;

    /// <summary>
    /// Creates the requested initial palette and packed storage when more than one value is supported.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Packed storage is required but the maximum entry count is not positive.
    /// </exception>
    public DataContainer(byte initialBitsPerEntry, byte minBitsPerEntry, byte maxBitsPerEntry, int maxEntryCount,
        Func<byte, IPalette<T>> paletteFactory)
        : this(minBitsPerEntry, maxBitsPerEntry, maxEntryCount, paletteFactory)
    {
        this.Palette = this.PaletteFactory(initialBitsPerEntry);

        if (!this.IsSingleValued)
        {
            if (this.MaxEntryCount <= 0)
                throw new InvalidOperationException("Cannot create a data array with a maximum entry count of 0 or less.");

            this.DataArray = new(this.MinBitsPerEntry, this.MaxEntryCount);
        }
    }

    /// <summary>
    /// Converts container-local coordinates to a packed storage index using the current bit width.
    /// </summary>
    public virtual int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    /// <summary>
    /// Holds the container's lock, so its palette and data can be read together while nothing writes them (saving).
    /// </summary>
    internal Lock.Scope EnterScope() => this.dataLock.EnterScope();

    /// <summary>
    /// Empties the container, as it was created: a single value palette without a value.
    /// </summary>
    internal void Reset()
    {
        lock (this.dataLock)
        {
            this.Palette = this.PaletteFactory(0);
            this.DataArray = null;
        }

        this.MarkUnsaved();
    }

    /// <summary>
    /// Expands packed storage when the palette requires it, returning whether storage changed.
    /// </summary>
    public bool TryGrow()
    {
        if (this.Palette is SingleValuePalette<T> singleValuePalette)
        {
            if (!singleValuePalette.ShouldGrow)
                return false;

            this.Palette = this.PaletteFactory(this.MinBitsPerEntry);
            this.Palette.GetOrAddId(singleValuePalette.Value);//Add the old value

            this.DataArray = new(this.MinBitsPerEntry, this.MaxEntryCount);//Initialize the data array

            return true;
        }

        //This should never happen, but just in case
        if (this.DataArray is null)
            throw new UnreachableException("Data array in unintialized.");

        if (Palette.BitCount <= DataArray.BitsPerEntry)
            return false;

        DataArray = DataArray.Grow(Palette.BitCount);

        return true;
    }

    /// <summary>
    /// Sets a value at container-local coordinates under the data lock and marks the container unsaved.
    /// </summary>
    public virtual void Set(int x, int y, int z, T value)
    {
        lock (this.dataLock)
        {
            var index = GetIndex(x, y, z);

            int paletteId = this.Palette.GetOrAddId(value);

            if (this.TryGrow())
                paletteId = this.Palette.GetOrAddId(value);

            if (!this.IsSingleValued)
                this.DataArray[index] = paletteId;
        }

        this.MarkUnsaved();
    }

    /// <summary>
    /// Adds a value to the palette, growing storage if needed, and marks the container unsaved.
    /// </summary>
    public virtual void Add(T value)
    {
        lock (this.dataLock)
        {
            int paletteId = this.Palette.GetOrAddId(value);

            if (this.TryGrow())
                this.Palette.GetOrAddId(value);
        }

        this.MarkUnsaved();
    }

    /// <summary>
    /// Reads a value at container-local coordinates under the data lock.
    /// </summary>
    public virtual T Get(int x, int y, int z)
    {
        lock (this.dataLock)
        {
            if (this.IsSingleValued)
                return this.Palette.GetValueFromIndex(0);

            int storageId = this.DataArray[GetIndex(x, y, z)];

            return this.Palette.GetValueFromIndex(storageId);
        }
    }

    /// <summary>
    /// Writes the bit width, palette and any packed data to the network stream.
    /// </summary>
    public virtual void WriteTo(INetStreamWriter writer)
    {
        writer.WriteByte(BitsPerEntry);

        Palette.WriteTo(writer);

        if (!this.IsSingleValued)
            writer.WriteLongArray(DataArray.storage);
    }

    /// <summary>
    /// Creates a copy of this container's palette and packed values.
    /// </summary>
    public abstract DataContainer<T> Clone();
}
