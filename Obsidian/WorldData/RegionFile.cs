using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class RegionFile : IAsyncDisposable
{
    private const int HeaderTableSize = 1024;
    private const int SectionSize = 4096;

    private readonly string filePath;
    private readonly int x;
    private readonly int z;
    private readonly int cubicRegionSize;
    private readonly int op;
    private readonly SemaphoreSlim semaphore = new(1, 1);

    private FileStream regionFileStream;
    private IMemoryOwner<byte> chunkCache;

    private bool disposed;

    public int[] Locations { get; private set; } = new int[HeaderTableSize];
    public int[] Timestamps { get; private set; } = new int[HeaderTableSize];

    /// <summary>
    /// Reference Material: https://wiki.vg/Region_Files#Structure
    /// </summary>
    public RegionFile(string filePath, int cubicRegionSize = 32)
    {
        var name = Path.GetFileName(filePath);
        var split = name.Split('.');

        this.filePath = filePath;
        this.x = int.Parse(split[1]);
        this.z = int.Parse(split[2]);
        this.cubicRegionSize = cubicRegionSize;

        this.op = cubicRegionSize - 1;
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            this.regionFileStream = new(this.filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch { throw; }

        if (regionFileStream.Length == 0)
            return true;

        this.chunkCache = MemoryPool<byte>.Shared.Rent((int)this.regionFileStream.Length - SectionSize * 2);

        for (var index = 0; index < HeaderTableSize; index++)
        {
            using var num = new RentedArray<byte>(4);

            await this.regionFileStream.ReadAsync(num);

            this.Locations[index] = BinaryPrimitives.ReadInt32BigEndian(num);
        }

        for (var index = 0; index < HeaderTableSize; index++)
        {
            using var num = new RentedArray<byte>(4);

            await this.regionFileStream.ReadAsync(num);

            this.Timestamps[index] = BinaryPrimitives.ReadInt32BigEndian(num);
        }

        await this.regionFileStream.ReadAsync(this.chunkCache.Memory);

        return true;
    }

    public async Task SetChunkAsync(int x, int z, byte[] bytes)
    {
        var tableIndex = this.GetChunkTableIndex(x, z);

        var (offset, size) = this.GetLocation(tableIndex);

        if (offset == 0 && size == 0)
        {
            await this.WriteNewChunkAsync(bytes, tableIndex);

            return;
        }

        this.SetLocation(tableIndex, offset, bytes.Length);

        await this.WriteHeadersAsync();

        this.regionFileStream.Position = offset;

        await this.WriteChunkHeaderAsync(bytes.Length + 1, 0x02);

        await this.regionFileStream.WriteAsync(bytes);

        this.Pad();

        await this.UpdateChunkCache();
    }

    public ReadOnlyMemory<byte> GetChunkCompressedBytes(int x, int z)
    {
        var tableIndex = this.GetChunkTableIndex(x, z);

        var (offset, size) = this.GetLocation(tableIndex);

        if (offset == 0 && size == 0)
            return ReadOnlyMemory<byte>.Empty;

        var chunk = this.chunkCache.Memory[(offset - (SectionSize * 2))..];

        var length = BinaryPrimitives.ReadInt32BigEndian(chunk.Span[..4]);

        var compression = chunk.Span[4];//We'll probably make use of this eventually

        if (length > size)
            throw new InvalidOperationException($"{length} > {size}");

        return chunk.Slice(5, length - 1);//Compression is included with the length
    }

    public async Task FlushAsync() =>
       await this.regionFileStream.FlushAsync();

    private async Task WriteNewChunkAsync(byte[] bytes, int tableIndex)
    {
        var size = this.CalculateChunkSize(bytes.Length);

        var offset = this.regionFileStream.Length > 0 ? (int)this.regionFileStream.Length : SectionSize * 2;

        this.SetLocation(tableIndex, offset / SectionSize, size);
        this.SetTimestamp(tableIndex, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await this.WriteHeadersAsync();

        this.regionFileStream.Position = offset;

        await this.WriteChunkHeaderAsync(bytes.Length + 1, 0x02);

        await this.regionFileStream.WriteAsync(bytes);

        this.Pad();

        await this.UpdateChunkCache();
    }

    private async Task UpdateChunkCache()
    {
        this.chunkCache?.Dispose();

        this.chunkCache = MemoryPool<byte>.Shared.Rent((int)this.regionFileStream.Length - SectionSize * 2);

        this.regionFileStream.Position = SectionSize * 2;

        await this.regionFileStream.ReadAsync(this.chunkCache.Memory);
    }

    private void Pad()
    {
        var missing = this.regionFileStream.Length % SectionSize;

        if (missing > 0)
            this.regionFileStream.SetLength(this.regionFileStream.Length + SectionSize - missing);
    }

    private async Task WriteChunkHeaderAsync(int length, byte compression)
    {
        using var bytes = new RentedArray<byte>(4);

        BinaryPrimitives.WriteInt32BigEndian(bytes.Span, length + 1);//Write length and include compression byte

        await this.regionFileStream.WriteAsync(bytes);

        this.regionFileStream.WriteByte(compression);// Write compression
    }

    private void SetTimestamp(int tableIndex, int time) =>
        this.Timestamps[tableIndex] = time;

    private void SetLocation(int tableIndex, int offset, int size) =>
        this.Locations[tableIndex] = (offset << 8) | (size & 0xFF);

    private int CalculateChunkSize(long length) =>
        (int)Math.Ceiling(length / (double)SectionSize);

    private int GetChunkTableIndex(int x, int z) =>
        (x & this.op) + (z & this.op) * this.cubicRegionSize;

    private (int offset, int size) GetLocation(int tableIndex)
    {
        var sector = this.Locations[tableIndex];

        var offset = sector >> 8;
        var size = sector & 0xFF;

        return (offset * SectionSize, size * SectionSize);
    }

    private async Task WriteHeadersAsync()
    {
        this.regionFileStream.Position = 0;

        await using var ms = new MemoryStream();

        for (var index = 0; index < HeaderTableSize; index++)
        {
            using var mem = new RentedArray<byte>(4);

            BinaryPrimitives.WriteInt32BigEndian(mem.Span, this.Locations[index]);

            await ms.WriteAsync(mem);
        }

        for (var index = 0; index < HeaderTableSize; index++)
        {
            using var mem = new RentedArray<byte>(4);

            BinaryPrimitives.WriteInt32BigEndian(mem.Span, this.Timestamps[index]);

            await ms.WriteAsync(mem);
        }

        ms.Position = 0;

        await ms.CopyToAsync(this.regionFileStream);
    }

    #region IDisposable
    private async Task DisposeAsync(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                await this.regionFileStream.FlushAsync();
                await this.regionFileStream.DisposeAsync();

                this.chunkCache.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            this.disposed = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~RegionFile()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public async ValueTask DisposeAsync()
    {
        // Do not change this code. Put cleanup code in 'DisposeAsync(bool disposing)' method
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }
    #endregion IDisposable
}
