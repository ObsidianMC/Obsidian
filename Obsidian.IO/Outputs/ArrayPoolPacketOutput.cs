using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Obsidian.IO.Outputs;

/// <summary>
/// Writes bytes to an <see cref="ArrayPool{T}"/>, written bytes can be accessed through <see cref="WrittenBytes"/>
/// </summary>
public struct ArrayPoolPacketOutput : IPacketOutput
{
    private byte[] poolBuffer;
    private int bufferLength;

    public ArrayPool<byte> Pool { get; }

    public ReadOnlySpan<byte> WrittenBytes => poolBuffer.AsSpan(0, bufferLength);

    public long Written => bufferLength;

    private const int InitialArraySize = 256;
    private const int ArrayGrowthFactor = 2;

    public ArrayPoolPacketOutput(ArrayPool<byte> pool)
    {
        Pool = pool;
        poolBuffer = Pool.Rent(InitialArraySize);
        bufferLength = 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void WriteByte(byte b)
    {
        if (RequiresGrowth(sizeof(byte))) GrowBuffer(sizeof(byte));
        GetTail() = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public void WriteBytes(in ReadOnlySpan<byte> bytes)
    {
        var len = bytes.Length;
        if (RequiresGrowth(len)) GrowBuffer(len);
        var span = MemoryMarshal.CreateSpan(
            ref GetTail(),
            len);
        bytes.CopyTo(span);
        bufferLength += len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public bool TryGetDirectBuffer(int length, out Span<byte> buffer)
    {
        if (RequiresGrowth(length)) GrowBuffer(length);

        buffer = MemoryMarshal.CreateSpan(
            ref GetTail(),
            length);

        bufferLength += length;
        return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private bool RequiresGrowth(int required) => bufferLength + required > poolBuffer.Length;

    
    [MethodImpl(MethodImplOptions.NoInlining)]
    [SkipLocalsInit]
    private void GrowBuffer(int required)
    {
        const int alignment = 8;
        var alignedRequired = (required + (alignment - 1)) & ~(alignment - 1);
        var newBuffer = Pool.Rent((poolBuffer.Length + alignedRequired) * ArrayGrowthFactor);
        poolBuffer.CopyTo(newBuffer, 0);
        Pool.Return(poolBuffer);
        poolBuffer = newBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    private ref byte GetTail()
    {
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(this.poolBuffer), this.bufferLength);
    }

    public void Dispose()
    {
        Pool?.Return(poolBuffer);
    }
}
