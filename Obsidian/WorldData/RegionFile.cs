using Microsoft.Extensions.Logging;
using Obsidian.Nbt;
using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class RegionFile : IAsyncDisposable
{
    internal static ILogger logger;

    private const int HeaderTableSize = 1024;
    private const int SectorSize = 4096;
    private const int MaxSectorSize = 256;

    private readonly string filePath;

    private readonly int cubicRegionSize;
    private readonly int op;

    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly FileStream regionFileStream;

    private bool disposed;
    private bool initialized;

    private bool[] freeSectors = Array.Empty<bool>();

    public int[] Locations { get; private set; } = new int[HeaderTableSize];
    public int[] Timestamps { get; private set; } = new int[HeaderTableSize];

    public long EndOfFile => this.regionFileStream.Length;

    public NbtCompression Compression { get; }

    /// <summary>
    /// Reference Material: https://wiki.vg/Region_Files#Structure
    /// </summary>
    public RegionFile(string filePath, NbtCompression compression, int cubicRegionSize = 32)
    {
        this.filePath = filePath;
        Compression = compression;
        this.cubicRegionSize = cubicRegionSize;

        this.op = cubicRegionSize - 1;

        this.regionFileStream = new(this.filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public async Task<bool> InitializeAsync()
    {
        if (this.initialized)
            throw new InvalidOperationException("Region file has already been initialized.");

        this.initialized = true;

        if (regionFileStream.Length == 0)
        {
            await this.WriteHeadersAsync();

            this.Pad();

            this.UpdateFreeSectors();

            return true;
        }

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

        this.UpdateFreeSectors();

        return true;
    }

    public async Task SetChunkAsync(int x, int z, Memory<byte> bytes)
    {
        await this.semaphore.WaitAsync();

        var chunkSectorSize = this.CalculateSectorSize(bytes.Length);

        if (chunkSectorSize > MaxSectorSize)
            throw new InvalidOperationException($"{nameof(bytes)} calculated length({chunkSectorSize}) exceeds the max section size({MaxSectorSize})");

        var chunkSectorSizeBytesLength = chunkSectorSize * SectorSize;
        var tableIndex = this.GetChunkTableIndex(x, z);

        var (offset, size) = this.GetLocation(tableIndex);

        this.ResetPosition();

        if (offset == 0 && size == 0)
        {
            await this.WriteChunkAsync(new()
            {
                Start = (int)(this.EndOfFile / SectorSize),
                Size = chunkSectorSizeBytesLength,
                TableIndex = tableIndex,
                ChunkData = bytes
            });

            this.semaphore.Release();

            return;
        }

        if (chunkSectorSizeBytesLength > size)
        {
            logger?.LogDebug("Chunk exceeded original size({oldSize}). Attempting resize to ({newSize}). Old Offset: {offset}", size, chunkSectorSizeBytesLength, offset);

            offset = this.FindFreeSector(chunkSectorSize);

            if (offset == -1)
                offset = this.EndOfFile;

            logger?.LogDebug("New offset: {offset}", offset);
        }

        await this.WriteChunkAsync(new()
        {
            Start = (int)(offset / SectorSize),
            Size = chunkSectorSizeBytesLength,
            TableIndex = tableIndex,
            ChunkData = bytes
        });

        this.semaphore.Release();
    }

    public async Task<Memory<byte>?> GetChunkBytesAsync(int x, int z)
    {
        await this.semaphore.WaitAsync();

        var tableIndex = this.GetChunkTableIndex(x, z);

        var (offset, size) = this.GetLocation(tableIndex);

        if (offset == 0 && size == 0)
        {
            this.semaphore.Release();

            return null;
        }

        this.regionFileStream.Position = offset;

        using var chunk = new RentedArray<byte>(size);

        await this.regionFileStream.ReadAsync(chunk);

        var length = BinaryPrimitives.ReadInt32BigEndian(chunk.Span[..4]);

        if (length == 0)
            throw new UnreachableException("Chunk size header value returned 0.");

        this.semaphore.Release();

        if (length > size)
            throw new UnreachableException($"{length} > {size}");

        return chunk.Memory.Slice(5, length - 1);
    }

    public async Task FlushAsync()
    {
        this.Pad();
        await this.regionFileStream.FlushAsync();
    }

    private async Task WriteChunkAsync(Sector sector)
    {
        var offset = sector.Start * SectorSize;
        var pad = offset == this.EndOfFile;

        using var mem = new RentedArray<byte>(sector.Size);

        this.SetTimestamp(sector.TableIndex, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        this.SetLocation(sector.TableIndex, sector.Start, sector.ChunkSectorSize);

        await this.WriteHeadersAsync();

        BinaryPrimitives.WriteInt32BigEndian(mem.Span[..4], sector.ChunkData.Length + 1);

        mem.Span[4] = (byte)this.Compression;

        sector.ChunkData.Span.CopyTo(mem.Span[5..]);

        this.regionFileStream.Position = offset;
        await this.regionFileStream.WriteAsync(mem);

        if (pad)
            this.Pad();

        this.SetUsedSector(sector.Start, sector.ChunkSectorSize);
    }

    private async Task WriteHeadersAsync()
    {
        this.regionFileStream.Position = 0;

        for (var index = 0; index < HeaderTableSize; index++)
        {
            using var mem = new RentedArray<byte>(4);

            BinaryPrimitives.WriteInt32BigEndian(mem.Span, this.Locations[index]);

            await this.regionFileStream.WriteAsync(mem);
        }

        for (var index = 0; index < HeaderTableSize; index++)
        {
            using var mem = new RentedArray<byte>(4);

            BinaryPrimitives.WriteInt32BigEndian(mem.Span, this.Timestamps[index]);

            await this.regionFileStream.WriteAsync(mem);
        }
    }

    private int FindFreeSector(int sectorCount)
    {
        for (int i = 0; i < this.freeSectors.Length - sectorCount; i++)
        {
            var occupied = false;
            for (int j = 0; j < sectorCount; j++)
            {
                var offset = i + j;
                if (!this.freeSectors[offset])
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied)
                return i * SectorSize;
        }

        return -1;
    }

    private void UpdateFreeSectors()
    {
        var fileSectorSize = (int)this.regionFileStream.Length / SectorSize;

        if (this.freeSectors.Length != fileSectorSize)
            Array.Resize(ref this.freeSectors, fileSectorSize);

        for (int i = 0; i < this.freeSectors.Length; i++)
            this.freeSectors[i] = i != 0 && i != 1;//First 2 sectors always occupied

        for (int i = 0; i < HeaderTableSize; i++)
        {
            var (offset, size) = this.GetLocation(i);

            var sectorStart = offset / SectorSize;
            var chunkSectorSize = size / SectorSize;

            if (size == 0 && sectorStart + chunkSectorSize >= this.freeSectors.Length)
                continue;

            for (int sectorCount = 0; sectorCount < chunkSectorSize; sectorCount++)
                this.freeSectors[sectorCount + sectorStart] = false;
        }
    }

    private void SetUsedSector(int sectorStart, int sectorCount)
    {
        var missedSectors = 0;

        for (int i = sectorStart; i < sectorStart + sectorCount; i++)
        {
            if (i < this.freeSectors.Length)
                this.freeSectors[i] = false;
            else
                missedSectors++;
        }

        if (missedSectors > 0)
        {
            Array.Resize(ref this.freeSectors, this.freeSectors.Length + missedSectors);
            this.SetUsedSector(sectorStart, sectorCount);
        }
    }

    private (long offset, int size) GetLocation(int tableIndex)
    {
        var sector = this.Locations[tableIndex];

        var offset = sector >> 8;
        var size = sector & 0xFF;

        return (offset * SectorSize, size * SectorSize);
    }

    private int GetChunkTableIndex(int x, int z) =>
        (x & this.op) + (z & this.op) * this.cubicRegionSize;

    private void SetTimestamp(int tableIndex, int time) =>
        this.Timestamps[tableIndex] = time;

    private void SetLocation(int tableIndex, int offset, int size) =>
         this.Locations[tableIndex] = (offset << 8) | (size & 0xFF);

    private int CalculateSectorSize(int length) =>
        (int)Math.Ceiling((length + 5) / (double)SectorSize);

    private void Pad()
    {
        var missing = this.EndOfFile % SectorSize;

        if (missing == 0)
            return;

        this.regionFileStream.SetLength(this.EndOfFile + (SectorSize - missing));
    }

    private void ResetPosition() => this.regionFileStream.Position = SectorSize * 2;

    #region IDisposable
    private async Task DisposeAsync(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                await this.FlushAsync();
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

    private readonly struct Sector
    {
        /// <summary>
        /// The start of the sector. 
        /// </summary>
        /// <remarks>
        /// To get the start of a sector you must divide the file offset by the default sector size. e.x (offset / 4096)
        /// </remarks>
        public required int Start { get; init; }

        /// <summary>
        /// The size of this sector.
        /// </summary>
        public required int Size { get; init; }

        /// <summary>
        /// The index of where the data is located in the <seealso cref="RegionFile.Locations"/> table.
        /// </summary>
        public required int TableIndex { get; init; }

        /// <summary>
        /// The chunk data.
        /// </summary>
        public required Memory<byte> ChunkData { get; init; }

        public int ChunkSectorSize => this.Size / RegionFile.SectorSize;
    }
}
