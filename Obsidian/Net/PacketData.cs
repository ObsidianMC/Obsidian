using System.Buffers;

namespace Obsidian.Net;
public readonly struct PacketData : IDisposable
{
    public static readonly PacketData Default = new() { Id = -1, Data = [] };

    public required int Id { get; init; }

    public byte[] Data { get; init; }

    public bool IsDisposable { get; init; }

    public void Deconstruct(out int id, out byte[] data)
    {
        id = this.Id;
        data = this.Data;
    }

    public void Dispose()
    {
        if (this.IsDisposable)
            ArrayPool<byte>.Shared.Return(this.Data);
    }
}
