using System.Runtime.CompilerServices;

namespace Obsidian.IO.Outputs;

/// <summary>
/// Writes bytes to the specified <see cref="Stream"/>
/// </summary>
public struct StreamPacketOutput : IPacketOutput
{
    public Stream Stream { get; }
    private int written;

    public long Written => written;

    public StreamPacketOutput(Stream stream)
    {
        Stream = stream;
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void WriteByte(byte b)
    {
        Stream.WriteByte(b);
        written++;   
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void WriteBytes(in ReadOnlySpan<byte> bytes)
    {
        Stream.Write(bytes);
        written += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public bool TryGetDirectBuffer(int length, out Span<byte> buffer)
    {
        // Ths could work if we make the stream a generic parameter, and check for MemoryStream
        buffer = default;
        return false;
    }
    
    public void Dispose()
    {
        
    }
}
