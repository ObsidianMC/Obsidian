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


    public ArrayPoolPacketOutput(ArrayPool<byte> pool)
    {
        Pool = pool;
        poolBuffer = Pool.Rent(1);
        bufferLength = 0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteByte(byte b)
    {
        if (RequiresGrowth(1)) GrowBuffer();
        GetTail() = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public void WriteBytes(in ReadOnlySpan<byte> bytes)
    {
        var len = bytes.Length;
        if (RequiresGrowth(len)) GrowBuffer();
        var span = MemoryMarshal.CreateSpan(
            ref GetTail(),
            len);
        bytes.CopyTo(span);
        bufferLength += len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public bool TryGetDirectBuffer(int length, out Span<byte> buffer)
    {
        if (RequiresGrowth(length)) GrowBuffer();

        buffer = MemoryMarshal.CreateSpan(
            ref GetTail(),
            length);

        bufferLength += length;
        return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private bool RequiresGrowth(int required) => bufferLength + required > this.poolBuffer.Length;

    
    private void GrowBuffer()
    {
        var newBuffer = Pool.Rent(poolBuffer.Length * 2);
        poolBuffer.CopyTo(newBuffer, 0);
        Pool.Return(poolBuffer);
        poolBuffer = newBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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
