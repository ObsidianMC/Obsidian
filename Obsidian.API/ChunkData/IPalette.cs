namespace Obsidian.API.ChunkData;
/// <summary>
/// Maps container values to the integer identifiers stored in a paletted section.
/// </summary>
public interface IPalette<T>
{
    /// <summary>
    /// Gets the number of values held by this palette.
    /// </summary>
    public int Count { get; }
    /// <summary>
    /// Gets the number of bits needed for a stored palette index.
    /// </summary>
    public int BitCount { get; }
    /// <summary>
    /// Gets whether this palette has reached its capacity.
    /// </summary>
    public bool IsFull { get; }

    /// <summary>
    /// Gets whether the container must expand to represent an added value.
    /// </summary>
    public bool ShouldGrow { get; }

    /// <summary>
    /// Looks up a value's palette identifier, returning whether it was found.
    /// </summary>
    public bool TryGetId(T value, out int id);
    /// <summary>
    /// Gets a value's identifier, adding it or requesting palette growth when necessary.
    /// </summary>
    public int GetOrAddId(T value);
    /// <summary>
    /// Gets the value represented by a stored palette index.
    /// </summary>
    public T? GetValueFromIndex(int index);

    /// <summary>
    /// Creates a copy of this palette.
    /// </summary>
    public IPalette<T> Clone();

    /// <summary>
    /// Writes the palette's representation to the network stream.
    /// </summary>
    public void WriteTo(INetStreamWriter writer);
}
