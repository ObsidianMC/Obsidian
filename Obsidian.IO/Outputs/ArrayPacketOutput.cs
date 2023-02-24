using System.Runtime.CompilerServices;

namespace Obsidian.IO.Outputs;

/// <summary>
/// Writes data to the specified <see cref="Buffer"/>, will not resize iit
/// </summary>
public struct ArrayPacketOutput : IPacketOutput
{
    public byte[] Buffer { get; }
    
    public long Written => written;

    private int written;
    
    public ReadOnlySpan<byte> WrittenBytes => Buffer.AsSpan(0, written);


    public ArrayPacketOutput(byte[] buffer)
    {
        Buffer = buffer;
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void WriteByte(byte b)
    {
        Buffer[written++] = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void WriteBytes(in ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(Buffer.AsSpan(written));
        written += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public bool TryGetDirectBuffer(int length, out Span<byte> buffer) {
        if (written + length > Buffer.Length) {
            buffer = default;
            return false;
        }
        buffer = Buffer.AsSpan(written, length);
        written += length;
        return true;
    }

    public void Dispose()
    {
    }
}
