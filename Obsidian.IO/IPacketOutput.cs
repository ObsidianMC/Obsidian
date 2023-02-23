namespace Obsidian.IO;

/// <summary>
/// Represents any type to which a packet can be written.
/// </summary>
public interface IPacketOutput : IDisposable
{
    
    /// <summary>
    /// The number of <see cref="byte"/> written to the output.
    /// </summary>
    long Written { get; }
    
    /// <summary>
    /// Writes a single <see cref="byte"/> to the output.
    /// </summary>
    /// <param name="b"></param>
    void WriteByte(byte b);
    
    /// <summary>
    /// Writes a <see cref="Span{T}"/> of <see cref="byte"/> to the output.
    /// </summary>
    /// <param name="bytes"></param>
    void WriteBytes(in ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Tries to get a <see cref="byte"/> <see cref="Span{T}"/> of the specified <paramref name="length"/> from the output buffer, if possible.
    /// <para>
    /// The <paramref name="length"/> is committed to <see cref="Written"/> if this method returns <see langword="true"/>.
    /// </para>
    /// </summary>
    /// <param name="length">Length of the buffer to get.</param>
    /// <param name="buffer">The buffer to write to.</param>
    /// <returns><see langword="true"/> if the buffer was successfully retrieved, <see langword="false"/> otherwise.</returns>
    bool TryGetDirectBuffer(int length, out Span<byte> buffer);
}
