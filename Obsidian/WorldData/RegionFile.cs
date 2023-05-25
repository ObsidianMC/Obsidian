using Microsoft.Extensions.Logging;
using Obsidian.Nbt;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class RegionFile : IAsyncDisposable
{
    internal static ILogger logger;

    private const int headerTableSize = 1024;
    private const int sectionSize = 4096;
    private const int maxSectionSize = 256;

    private readonly string filePath;

    private readonly int cubicRegionSize;
    private readonly int op;

    private readonly SemaphoreSlim semaphore = new(1, 1);

    private FileStream regionFileStream;

    private bool disposed;
    private bool initialized;

    public int[] Locations { get; private set; } = new int[headerTableSize];
    public int[] Timestamps { get; private set; } = new int[headerTableSize];

    /// <summary>
    /// Reference Material: https://wiki.vg/Region_Files#Structure
    /// </summary>
    public RegionFile(string filePath, int cubicRegionSize = 32)
    {
        this.filePath = filePath;
        this.cubicRegionSize = cubicRegionSize;

        this.op = cubicRegionSize - 1;
    }

    public async Task<bool> InitializeAsync()
    {
        if (this.initialized)
            throw new InvalidOperationException("Region file has already been initialized.");

        try
        {
            this.regionFileStream = new(this.filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch { throw; }

        this.initialized = true;

        if (regionFileStream.Length == 0)
            return true;

        for (var index = 0; index < headerTableSize; index++)
        {
            using var num = new RentedArray<byte>(4);

            await this.regionFileStream.ReadAsync(num);

            this.Locations[index] = BinaryPrimitives.ReadInt32BigEndian(num);
        }

        for (var index = 0; index < headerTableSize; index++)
        {
            using var num = new RentedArray<byte>(4);

            await this.regionFileStream.ReadAsync(num);

            this.Timestamps[index] = BinaryPrimitives.ReadInt32BigEndian(num);
        }

        return true;
    }

    public async Task SetChunkAsync(int x, int z, byte[] bytes, NbtCompression compression = NbtCompression.ZLib)
    {
        await this.semaphore.WaitAsync();

        var chunkSectionSize = this.CalculateChunkSize(bytes.LongLength);

        if (chunkSectionSize > maxSectionSize)
            throw new InvalidOperationException($"{nameof(bytes)} calculated length({chunkSectionSize}) exceeds the max section size({maxSectionSize})");

        var tableIndex = this.GetChunkTableIndex(x, z);

        var (offset, size) = this.GetLocation(tableIndex);

        if (offset == 0 && size == 0)
        {
            await this.WriteNewChunkAsync(bytes, chunkSectionSize, tableIndex);

            this.semaphore.Release();

            return;
        }

        using var mem = new RentedArray<byte>(size);

        if (chunkSectionSize * sectionSize > size)// gotta allocate new sector now
        {
            //TODO figure out a cleaner way to push down the old chunk and get rid of its old sector

            var (previousOffset, _) = this.GetLocation(tableIndex - 1);

            this.SetLocation(tableIndex - 1, previousOffset, chunkSectionSize);

            await this.regionFileStream.WriteAsync(mem);

            await this.WriteNewChunkAsync(bytes, chunkSectionSize, tableIndex);

            this.semaphore.Release();

            return;
        }

        this.SetTimestamp(tableIndex, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await this.WriteHeadersAsync();

        BinaryPrimitives.WriteInt32BigEndian(mem.Span[..4], bytes.Length + 1);

        mem.Span[4] = (byte)compression;

        bytes.CopyTo(mem.Span[5..]);
        this.ResetPosition();
        this.regionFileStream.Position += offset;
        await this.regionFileStream.WriteAsync(mem);

        this.semaphore.Release();
    }

    private void ResetPosition() => this.regionFileStream.Position = sectionSize * 2;

    public async Task<ChunkBuffer?> GetChunkBytesAsync(int x, int z)
    {
        await this.semaphore.WaitAsync();

        var tableIndex = this.GetChunkTableIndex(x, z);

        var (offset, size) = this.GetLocation(tableIndex);

        if (offset == 0 && size == 0)
        {
            this.semaphore.Release();

            return null;
        }

        this.ResetPosition();
        this.regionFileStream.Position += offset;

        var chunk = new Memory<byte>(new byte[size]);

        await this.regionFileStream.ReadAsync(chunk);

        var length = BinaryPrimitives.ReadInt32BigEndian(chunk.Span[..4]);

        var compression = (NbtCompression)chunk.Span[4];

        this.semaphore.Release();

        if (length > size)
            throw new UnreachableException($"{length} > {size}");

        return new()
        {
            Memory = chunk.Slice(5, length - 1),
            Compression = compression
        };
    }

    public async Task FlushAsync() => await this.regionFileStream.FlushAsync();

    private async Task WriteNewChunkAsync(byte[] bytes, int size, int tableIndex)
    {
        var offset = this.regionFileStream.Length > 0 ? (int)this.regionFileStream.Length : sectionSize * 2;

        this.SetLocation(tableIndex, offset / sectionSize, size);
        this.SetTimestamp(tableIndex, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await this.WriteHeadersAsync();

        this.ResetPosition();
        this.regionFileStream.Position += offset;

        await this.WriteChunkHeaderAsync(bytes.Length + 1, 0x02);

        await this.regionFileStream.WriteAsync(bytes);

        this.Pad();
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
        (int)Math.Ceiling((length + 5) / (double)sectionSize);

    private int GetChunkTableIndex(int x, int z) =>
       (x & this.op) + (z & this.op) * this.cubicRegionSize;

    private (int offset, int size) GetLocation(int tableIndex)
    {
        var sector = this.Locations[tableIndex];

        var offset = sector >> 8;
        var size = sector & 0xFF;

        return (offset * sectionSize, size * sectionSize);
    }

    private async Task WriteHeadersAsync()
    {
        this.regionFileStream.Position = 0;

        for (var index = 0; index < headerTableSize; index++)
        {
            using var mem = new RentedArray<byte>(4);

            BinaryPrimitives.WriteInt32BigEndian(mem.Span, this.Locations[index]);

            await this.regionFileStream.WriteAsync(mem);
        }

        for (var index = 0; index < headerTableSize; index++)
        {
            using var mem = new RentedArray<byte>(4);

            BinaryPrimitives.WriteInt32BigEndian(mem.Span, this.Timestamps[index]);

            await this.regionFileStream.WriteAsync(mem);
        }
    }

    private void Pad()
    {
        var length = (int)this.regionFileStream.Length;
        var missing = (length + sectionSize - 1) / sectionSize * sectionSize;

        if (length != missing)
            this.regionFileStream.SetLength(length + (missing - length));
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
                this.semaphore.Dispose();
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
