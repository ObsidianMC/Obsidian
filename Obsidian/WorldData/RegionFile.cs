using Microsoft.Win32.SafeHandles;
using Obsidian.Nbt;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A region file in vanilla's Anvil format: a header of 1024 chunk locations and 1024 timestamps, then the chunks, each
/// in a run of 4 KiB sectors starting with its length and compression type. Chunks too big for 255 sectors are kept in
/// an external <c>c.x.z.mcc</c> file beside the region file, like vanilla's.
/// </summary>
/// <remarks>
/// <para>
/// A chunk is never written over its previous version. Its new version goes to free sectors, then its location in
/// the header is switched to them: that 4-byte write is the commit point. A write that fails or is torn before it
/// leaves the header pointing at the previous version, whose sectors are untouched. The previous version's sectors
/// are only reused once the switch away from them was flushed to disk (see <see cref="FlushAsync"/>), so not even a
/// crash that loses unflushed writes can leave the header pointing at sectors that were already reused.
/// </para>
/// <para>
/// Payloads are read and written at their own offsets, outside the lock, which only guards the header and the
/// free-sector map. Writes of a chunk are taken in order with <see cref="ReserveWrite"/>.
/// </para>
/// </remarks>
public sealed class RegionFile : IAsyncDisposable
{
    /// <summary>
    /// The zlib (or gzip) level chunk payloads are compressed at. Vanilla reads every level the same; the fastest
    /// takes a fraction of the default's time for files a little bigger.
    /// </summary>
    internal const CompressionLevel PayloadCompressionLevel = CompressionLevel.Fastest;

    private const int HeaderTableSize = 1024;
    private const int SectorSize = 4096;
    private const int HeaderSectors = 2;
    private const int MaxInternalSectors = byte.MaxValue;

    // A chunk's payload starts with its length (counting the compression type) and its compression type.
    private const int PrefixSize = 5;

    // Set in the compression type of a chunk whose payload is in its external file.
    private const byte ExternalFlag = 0x80;

    // The least a chunk's NBT is decompressed into at first; it grows from there.
    private const int MinDecompressedCapacity = 64 * 1024;

    private static readonly ReadOnlyMemory<byte> zeroes = new byte[SectorSize];

    private readonly string filePath;

    private readonly int cubicRegionSize;
    private readonly int op;

    // The region's position, from the file's name, which names its chunks' external files; null when it has none.
    private readonly (int X, int Z)? regionPosition;

    private readonly SafeFileHandle handle;

    // Guards the header, the sector map, the retired runs and the file's length. Payload I/O happens outside it.
    private readonly SemaphoreSlim headerLock = new(1, 1);

    // The last write reserved for each chunk, which the next one waits for; guarded by itself.
    private readonly Task?[] lastWrites = new Task?[HeaderTableSize];

    private readonly int[] locations = new int[HeaderTableSize];
    private readonly int[] timestamps = new int[HeaderTableSize];

    // Whether each chunk's committed version is external, as far as this session knows.
    private readonly Storage[] storage = new Storage[HeaderTableSize];

    // Which sectors are in use, for the first sectorCount sectors; past them every sector is free.
    private bool[] usedSectors = [];
    private int sectorCount;

    // Runs of chunks' previous versions, kept until the header switches away from them are on disk.
    private List<RetiredRun> retiredSinceFlush = [];

    // Runs whose switch is on disk, kept until the reads that may still be reading them end.
    private readonly List<RetiredRun> retiredBehindReads = [];

    private int activeReads;
    private int activeWrites;

    private long fileLength;

    private bool disposed;
    private bool initialized;

    // Whether something was written since the file was last flushed to disk.
    private bool unflushed;

    /// <summary>
    /// The length of the file, as far as the writes that completed made it.
    /// </summary>
    public long EndOfFile => Volatile.Read(ref this.fileLength);

    public NbtCompression Compression { get; }

    /// <summary>
    /// Called with a chunk's local coordinates once its payload was written, before its header entry is switched; a
    /// test throwing from it interrupts the write where a crash would.
    /// </summary>
    internal Func<int, int, ValueTask>? PayloadWritten { get; init; }

    /// <summary>
    /// Reference Material: <see href="https://minecraft.wiki/w/Region_file_format">Region File Structure</see>
    /// </summary>
    public RegionFile(string filePath, NbtCompression compression, int cubicRegionSize = 32)
    {
        if (compression is not (NbtCompression.None or NbtCompression.GZip or NbtCompression.ZLib))
        {
            throw new ArgumentOutOfRangeException(nameof(compression), compression,
                "Region files hold gzip, zlib or uncompressed chunks.");
        }

        this.filePath = filePath;
        this.cubicRegionSize = cubicRegionSize;
        this.op = cubicRegionSize - 1;
        this.regionPosition = ParseRegionPosition(filePath);
        this.Compression = compression;

        this.handle = File.OpenHandle(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            FileOptions.Asynchronous);
    }

    public async Task<bool> InitializeAsync()
    {
        if (this.initialized)
            throw new InvalidOperationException("Region file has already been initialized.");

        this.initialized = true;
        this.fileLength = RandomAccess.GetLength(this.handle);

        var header = new byte[HeaderSectors * SectorSize];
        if (this.fileLength < header.Length)
        {
            // Interrupted creation can leave part of an empty header on disk.
            var existing = header.AsMemory(0, (int)this.fileLength);
            await RandomAccess.ReadAsync(this.handle, existing, 0);
            if (existing.Span.ContainsAnyExcept((byte)0))
                throw new InvalidDataException($"Region file '{filePath}' has a truncated header containing data ({existing.Length} bytes).");

            header.AsSpan().Clear();
            await RandomAccess.WriteAsync(this.handle, header, 0);
            this.fileLength = header.Length;
        }
        else
        {
            await RandomAccess.ReadAsync(this.handle, header, 0);
        }

        for (var index = 0; index < HeaderTableSize; index++)
        {
            this.locations[index] = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(index * sizeof(int)));
            this.timestamps[index] = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(SectorSize + index * 4));
        }

        this.BuildSectorMap();

        return true;
    }

    /// <summary>
    /// Writes the provided chunk data to the region file.
    /// </summary>
    /// <param name="chunkX">The chunks local x coordinate.</param>
    /// <param name="chunkZ">The chunks local z coordinate</param>
    /// <param name="chunkData">The serialized nbt data, compressed in <see cref="Compression"/>.</param>
    public async Task SetChunkAsync(int chunkX, int chunkZ, Memory<byte> chunkData)
    {
        using var write = this.ReserveWrite(chunkX, chunkZ);

        await write.TurnAsync;
        await this.WriteChunkAsync(chunkX, chunkZ, chunkData);
    }

    /// <summary>
    /// Writes the bytes written to <paramref name="written"/> to the region file, like
    /// <see cref="SetChunkAsync(int, int, Memory{byte})"/> but without copying them out of the stream first. The stream
    /// may be closed already (an NBT writer closes the stream it compressed into); don't touch it until this completes.
    /// </summary>
    internal Task SetChunkAsync(int chunkX, int chunkZ, MemoryStream written)
    {
        // TryGetBuffer still works on a closed stream, unlike Length.
        if (!written.TryGetBuffer(out var bytes))
            throw new ArgumentException("The stream's buffer isn't exposed.", nameof(written));

        return this.SetChunkAsync(chunkX, chunkZ, bytes.AsMemory());
    }

    /// <summary>
    /// Takes the next turn to write the chunk: the write waits for <see cref="ChunkWrite.TurnAsync"/>, which completes
    /// once every write reserved before it is disposed. Reserving writes as their contents are taken writes them in
    /// that order however long each takes to compress. Dispose the reservation once written, or once it won't be.
    /// </summary>
    internal ChunkWrite ReserveWrite(int chunkX, int chunkZ)
    {
        var tableIndex = this.GetChunkTableIndex(chunkX, chunkZ);
        lock (this.lastWrites)
        {
            var write = new ChunkWrite(this.lastWrites[tableIndex] ?? Task.CompletedTask);
            this.lastWrites[tableIndex] = write.Done;

            return write;
        }
    }

    /// <summary>
    /// Compresses a chunk's NBT as this file stores it, at <see cref="PayloadCompressionLevel"/>. Return the result to
    /// the pool once written.
    /// </summary>
    internal PooledBufferStream Compress(ReadOnlySpan<byte> nbt)
    {
        // Chunk NBT compresses several times over, so a quarter of it is a generous start.
        var compressed = new PooledBufferStream(Math.Max(nbt.Length / 4, SectorSize));
        try
        {
            if (this.Compression == NbtCompression.None)
            {
                compressed.Write(nbt);
                return compressed;
            }

            using (Stream compressor = this.Compression == NbtCompression.GZip
                ? new GZipStream(compressed, PayloadCompressionLevel, leaveOpen: true)
                : new ZLibStream(compressed, PayloadCompressionLevel, leaveOpen: true))
            {
                compressor.Write(nbt);
            }

            return compressed;
        }
        catch
        {
            compressed.Return();
            throw;
        }
    }

    /// <summary>
    /// Writes a chunk's compressed payload to free sectors and switches its header entry to them. Callers take their
    /// turn first (see <see cref="ReserveWrite"/>).
    /// </summary>
    internal async Task WriteChunkAsync(int chunkX, int chunkZ, ReadOnlyMemory<byte> payload)
    {
        var tableIndex = this.GetChunkTableIndex(chunkX, chunkZ);
        var compressionType = CompressionType(this.Compression);
        var sectorsNeeded = SectorsFor(PrefixSize + payload.Length);

        // Like vanilla, a chunk too big for its 255 sectors goes to its external file, and the region keeps a stub
        // (length 1, the compression type flagged external) in one sector. The file is written to a temporary first
        // and moved over the previous one as the write commits, so a failed write leaves the previous one.
        var external = sectorsNeeded > MaxInternalSectors;
        if (external)
        {
            compressionType |= ExternalFlag;
            sectorsNeeded = 1;
        }

        int start;
        bool extendsFile;
        await this.headerLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            (start, extendsFile) = this.Allocate(sectorsNeeded);
            this.activeWrites++;
        }
        finally
        {
            this.headerLock.Release();
        }

        // Whether this write stopped counting as running, and whether the header may point at its sectors.
        var ended = false;
        var switching = false;
        string? externalTemporary = null;
        try
        {
            if (external)
            {
                externalTemporary = await this.WriteExternalTemporaryAsync(chunkX, chunkZ, payload);
                payload = ReadOnlyMemory<byte>.Empty;
            }

            await this.WritePayloadAsync(start, sectorsNeeded, extendsFile, compressionType, payload);

            if (this.PayloadWritten is not null)
                await this.PayloadWritten(chunkX & this.op, chunkZ & this.op);

            await this.headerLock.WaitAsync();
            try
            {
                this.activeWrites--;
                ended = true;
                ObjectDisposedException.ThrowIf(this.disposed, this);

                var end = (long)(start + sectorsNeeded) * SectorSize;
                this.fileLength = Math.Max(this.fileLength, end);

                if (externalTemporary is not null)
                {
                    File.Move(externalTemporary, this.ExternalPath(chunkX, chunkZ), overwrite: true);
                    externalTemporary = null;
                }

                await this.WriteTimestampAsync(tableIndex);

                switching = true;
                await this.SwitchLocationAsync(tableIndex, start, sectorsNeeded, external);
            }
            finally
            {
                this.headerLock.Release();
            }
        }
        catch
        {
            await this.AbandonAsync(start, sectorsNeeded, freeSectors: !switching, endWrite: !ended);

            if (externalTemporary is not null)
                File.Delete(externalTemporary);

            throw;
        }
    }

    /// <summary>
    /// Reads through the region file to try and retrieve the compressed nbt chunk data from the region file.
    /// </summary>
    /// <param name="chunkX">The chunks local x coordinate.</param>
    /// <param name="chunkZ">The chunks local z coordinate</param>
    /// <returns>The uncompressed nbt chunk data, or null if no chunk was found at the specified coordinate.</returns>
    public async Task<Memory<byte>?> GetChunkBytesAsync(int chunkX, int chunkZ)
    {
        if (await this.RentChunkBytesAsync(chunkX, chunkZ) is not RentedArray<byte> rented)
            return null;

        using (rented)
            return rented.Span.ToArray();
    }

    /// <summary>
    /// Like <see cref="GetChunkBytesAsync"/>, but the uncompressed NBT is left in an array rented from the shared pool
    /// instead of copied into one of its own. Dispose it once the bytes are parsed; nothing may hold on to them after.
    /// </summary>
    internal async Task<RentedArray<byte>?> RentChunkBytesAsync(int chunkX, int chunkZ)
    {
        var tableIndex = this.GetChunkTableIndex(chunkX, chunkZ);

        // The location is taken under the lock, so it's one a write committed, never one being switched. Its sectors
        // aren't reused while the read is running (see ReleaseRetiredRuns), so they're read outside the lock.
        long offset;
        int size;
        await this.headerLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            (offset, size) = this.GetLocation(tableIndex);
            if (offset == 0 && size == 0)
                return null;

            this.ValidateLocation(tableIndex, offset, size);
            this.activeReads++;
        }
        finally
        {
            this.headerLock.Release();
        }

        try
        {
            return await this.ReadChunkAsync(chunkX, chunkZ, offset, size);
        }
        finally
        {
            await this.headerLock.WaitAsync();
            this.activeReads--;
            this.ReleaseRetiredRuns();
            this.headerLock.Release();
        }
    }

    /// <summary>
    /// Whether the region file has data for the chunk at the local coordinates.
    /// </summary>
    public bool HasChunk(int chunkX, int chunkZ) =>
        Volatile.Read(ref this.locations[this.GetChunkTableIndex(chunkX, chunkZ)]) != 0;

    /// <summary>
    /// Whether the region file at <paramref name="filePath"/> has any chunk: a location in its header that isn't zero.
    /// Creating a level can be interrupted after its region files were created but before chunks were written to them.
    /// </summary>
    internal static bool HasChunks(string filePath)
    {
        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var locations = new byte[HeaderTableSize * 4];
        var read = file.ReadAtLeast(locations, locations.Length, throwOnEndOfStream: false);

        return locations.AsSpan(0, read).ContainsAnyExcept((byte)0);
    }

    /// <summary>
    /// Flushes what was written to the disk, unless nothing was since the last flush or the file was closed, which
    /// flushed it. Waiting for the disk happens on the storage threads (see <see cref="WorkerThreads.Storage"/>), not on
    /// the caller's. The sectors of chunks' previous versions are free again after.
    /// </summary>
    public async Task FlushAsync()
    {
        await this.headerLock.WaitAsync();
        try
        {
            if (!this.disposed)
                await this.FlushUnderLockAsync();
        }
        finally
        {
            this.headerLock.Release();
        }
    }

    private async Task FlushUnderLockAsync()
    {
        if (!this.unflushed)
            return;

        // A write that failed at the end of the file can leave its last sector short; vanilla pads it on closing too.
        // Only while no write is running, which could be extending the file past it.
        var padded = this.fileLength;
        if (this.activeWrites == 0 && this.fileLength % SectorSize != 0)
            padded = SectorsFor(this.fileLength) * (long)SectorSize;

        await WorkerThreads.Storage.RunAsync(() =>
        {
            if (padded != this.fileLength)
                RandomAccess.SetLength(this.handle, padded);

            // Like vanilla's synced writes, this also has the operating system write its cache to the disk.
            RandomAccess.FlushToDisk(this.handle);
        });

        this.fileLength = padded;
        this.unflushed = false;

        // Every header switch that retired these runs is on the disk now, so no crash can bring back a header that
        // points at them: they can be reused once no read that may still be reading them is running.
        this.retiredBehindReads.AddRange(this.retiredSinceFlush);
        this.retiredSinceFlush = [];
        this.ReleaseRetiredRuns();
    }

    // Called under the lock. Vanilla only reports timestamps, so one written for a switch that fails loses nothing.
    private async Task WriteTimestampAsync(int tableIndex)
    {
        var timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var entry = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(entry, timestamp);

        await RandomAccess.WriteAsync(this.handle, entry, SectorSize + tableIndex * sizeof(int));
        this.timestamps[tableIndex] = timestamp;
        this.unflushed = true;
    }

    // The commit point, called under the lock once the payload is in free sectors. The location switches the chunk to
    // its new version in a single aligned 4-byte write, which a crash can't tear; until it, the header points at the
    // previous version, whose sectors nothing wrote to. Only the chunk's own entries are written, not the whole header.
    private async Task SwitchLocationAsync(int tableIndex, int start, int count, bool external)
    {
        var previous = this.locations[tableIndex];
        var location = (start << 8) | count;

        var entry = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(entry, location);

        await RandomAccess.WriteAsync(this.handle, entry, tableIndex * sizeof(int));

        // Readers take the location under the lock, so none sees it before its payload is complete.
        Volatile.Write(ref this.locations[tableIndex], location);

        var previousStorage = this.storage[tableIndex];
        this.storage[tableIndex] = external ? Storage.External : Storage.Internal;

        if (previous == 0)
            return;

        // The previous version's sectors are free once this switch is on disk. When it was (or may have been) external
        // and this one isn't, its external file goes with them, like vanilla deletes it.
        var deleteExternal = !external && previousStorage != Storage.Internal && this.regionPosition is not null;
        this.retiredSinceFlush.Add(new RetiredRun(previous >> 8, previous & 0xFF, deleteExternal ? tableIndex : -1));
    }

    // Gives back the sectors of a write that didn't commit, which the header never pointed at. When its location write
    // failed, whether it reached the file is unknown, so they stay used this session; the sector map is rebuilt from
    // the header when the file is opened again.
    private async Task AbandonAsync(int start, int count, bool freeSectors, bool endWrite)
    {
        await this.headerLock.WaitAsync();
        try
        {
            if (endWrite)
                this.activeWrites--;

            if (freeSectors && !this.disposed)
                this.MarkFree(start, count);
        }
        finally
        {
            this.headerLock.Release();
        }
    }

    // Frees the retired runs no read may still be reading. Called under the lock.
    private void ReleaseRetiredRuns()
    {
        if (this.activeReads != 0 || this.retiredBehindReads.Count == 0)
            return;

        foreach (var run in this.retiredBehindReads)
        {
            this.MarkFree(run.Start, run.Count);

            // Unless the chunk went external again since.
            if (run.ExternalOf >= 0 && this.storage[run.ExternalOf] == Storage.Internal)
            {
                var (x, z) = (run.ExternalOf % this.cubicRegionSize, run.ExternalOf / this.cubicRegionSize);
                DeleteStaleExternal(this.ExternalPath(x, z));
            }
        }

        this.retiredBehindReads.Clear();
    }

    // A stale external file is harmless, since its chunk's header says it's internal, so failing to delete it doesn't
    // fail the read or flush that released it.
    private static void DeleteStaleExternal(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task WritePayloadAsync(int start, int count, bool extendsFile, byte compressionType,
        ReadOnlyMemory<byte> payload)
    {
        var prefix = new byte[PrefixSize];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length + 1);
        prefix[4] = compressionType;

        // The prefix and the payload are written as they are; only a run that extends the file needs the rest of its
        // last sector, so the file stays a whole number of sectors.
        var buffers = new List<ReadOnlyMemory<byte>>(3) { prefix, payload };
        if (extendsFile)
        {
            var padding = count * SectorSize - PrefixSize - payload.Length;
            buffers.Add(zeroes[..padding]);
        }

        await RandomAccess.WriteAsync(this.handle, buffers, (long)start * SectorSize);
    }

    private async Task<string> WriteExternalTemporaryAsync(int chunkX, int chunkZ, ReadOnlyMemory<byte> payload)
    {
        if (this.regionPosition is null)
        {
            throw new NotSupportedException($"Chunk ({chunkX}, {chunkZ}) is too big for '{this.filePath}', whose "
                + "name doesn't give the region's position for an external chunk file.");
        }

        var temporary = $"{this.ExternalPath(chunkX, chunkZ)}.{Guid.NewGuid():N}.tmp";
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 0, FileOptions.Asynchronous))
        {
            await file.WriteAsync(payload);

            // On the disk before it's moved in, so the move never commits an external file a crash can empty.
            file.Flush(flushToDisk: true);
        }

        return temporary;
    }

    private async Task<RentedArray<byte>> ReadChunkAsync(int chunkX, int chunkZ, long offset, int size)
    {
        using var sectors = new RentedArray<byte>(size);
        var read = await RandomAccess.ReadAsync(this.handle, sectors.Memory, offset);
        var length = read < PrefixSize ? 0 : BinaryPrimitives.ReadInt32BigEndian(sectors.Span);

        // An external chunk's stub holds only its compression type; any other chunk holds a payload too.
        var compressionType = read < PrefixSize ? (byte)0 : sectors.Span[4];
        var minimum = (compressionType & ExternalFlag) == 0 ? 2 : 1;
        if (length < minimum || length > read - sizeof(int))
        {
            throw new InvalidDataException($"Region file '{this.filePath}', chunk ({chunkX}, {chunkZ}) has invalid "
                + $"length {length} for {size} allocated bytes.");
        }

        if ((compressionType & ExternalFlag) == 0)
            return this.Decompress(chunkX, chunkZ, compressionType, sectors.Memory.Slice(PrefixSize, length - 1));

        // Vanilla ignores the payload of a chunk marked external.
        if (this.regionPosition is null)
        {
            throw new InvalidDataException($"Region file '{this.filePath}', chunk ({chunkX}, {chunkZ}) is external, "
                + "but the file's name doesn't give the region's position.");
        }

        using var external = await ReadExternalAsync(this.ExternalPath(chunkX, chunkZ));
        return this.Decompress(chunkX, chunkZ, (byte)(compressionType & ~ExternalFlag), external.Memory);
    }

    private static async Task<RentedArray<byte>> ReadExternalAsync(string path)
    {
        // Shared for deleting, so a newer version can be moved over it while it's read.
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.Asynchronous);

        var bytes = new RentedArray<byte>((int)file.Length);
        try
        {
            await file.ReadExactlyAsync(bytes.Memory);
            return bytes;
        }
        catch
        {
            bytes.Dispose();
            throw;
        }
    }

    // Decompresses into a pooled array grown as needed, rather than a growing stream copied out at the end. The
    // compression is the chunk's own (1 gzip, 2 zlib, 3 none, like vanilla), not necessarily the file's.
    private RentedArray<byte> Decompress(int chunkX, int chunkZ, byte compressionType, ReadOnlyMemory<byte> compressed)
    {
        var pool = ArrayPool<byte>.Shared;

        if (compressionType == CompressionType(NbtCompression.None))
        {
            var copy = new RentedArray<byte>(compressed.Length, pool);
            compressed.Span.CopyTo(copy.Span);
            return copy;
        }

        using var compressedStream = new ReadOnlyStream(compressed);
        using Stream decompressor = compressionType switch
        {
            1 => new GZipStream(compressedStream, CompressionMode.Decompress),
            2 => new ZLibStream(compressedStream, CompressionMode.Decompress),
            _ => throw new InvalidDataException($"Region file '{this.filePath}', chunk ({chunkX}, {chunkZ}) has "
                + $"unsupported compression type {compressionType}.")
        };

        // Chunk NBT compresses several times over (light is mostly all 0 or all 15), so the first guess is generous.
        var buffer = pool.Rent(Math.Max(compressed.Length * 8, MinDecompressedCapacity));
        var length = 0;
        try
        {
            int read;
            while ((read = decompressor.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += read;
                if (length < buffer.Length)
                    continue;

                var larger = pool.Rent(buffer.Length * 2);
                buffer.AsSpan(0, length).CopyTo(larger);
                pool.Return(buffer);
                buffer = larger;
            }

            return new RentedArray<byte>(buffer, length, pool);
        }
        catch
        {
            pool.Return(buffer);
            throw;
        }
    }

    // Takes the first run of free sectors long enough, like vanilla, including one that ends the map, or else one at
    // the end of the file, which takes in the free sectors it ends with. Called under the lock.
    private (int Start, bool ExtendsFile) Allocate(int count)
    {
        var run = 0;
        var start = -1;
        for (var sector = HeaderSectors; sector < this.sectorCount; sector++)
        {
            run = this.usedSectors[sector] ? 0 : run + 1;
            if (run == count)
            {
                start = sector - count + 1;
                break;
            }
        }

        if (start < 0)
            start = this.sectorCount - run;

        this.MarkUsed(start, count);

        // Past the file's length, which may end within a sector, the run's last sector is padded.
        return (start, (long)(start + count) * SectorSize > this.fileLength);
    }

    private void BuildSectorMap()
    {
        this.sectorCount = Math.Max(HeaderSectors, SectorsFor(this.fileLength));
        this.usedSectors = new bool[this.sectorCount];
        this.MarkUsed(0, HeaderSectors);

        for (var index = 0; index < HeaderTableSize; index++)
        {
            var (offset, size) = this.GetLocation(index);
            if (offset == 0 && size == 0)
                continue;

            this.ValidateLocation(index, offset, size);
            this.MarkUsed((int)(offset / SectorSize), size / SectorSize);
        }
    }

    private void MarkUsed(int start, int count)
    {
        var end = start + count;
        if (end > this.usedSectors.Length)
            Array.Resize(ref this.usedSectors, Math.Max(end, this.usedSectors.Length * 2));

        this.sectorCount = Math.Max(this.sectorCount, end);
        this.usedSectors.AsSpan(start, count).Fill(true);
    }

    private void MarkFree(int start, int count)
    {
        var end = Math.Min(start + count, this.sectorCount);
        if (start < end)
            this.usedSectors.AsSpan(start, end - start).Clear();
    }

    private (long offset, int size) GetLocation(int tableIndex)
    {
        var sector = (uint)this.locations[tableIndex];

        var offset = sector >> 8;
        var size = sector & 0xFF;

        return ((long)offset * SectorSize, (int)size * SectorSize);
    }

    private void ValidateLocation(int tableIndex, long offset, int size)
    {
        if (offset >= HeaderSectors * SectorSize && size != 0 && offset <= this.fileLength - size)
            return;

        var (x, z) = (tableIndex % this.cubicRegionSize, tableIndex / this.cubicRegionSize);
        throw new InvalidDataException($"Region file '{this.filePath}', chunk ({x}, {z}) has invalid allocation at "
            + $"byte {offset} with length {size}; file length is {this.fileLength}.");
    }

    private int GetChunkTableIndex(int x, int z) =>
        (x & this.op) + (z & this.op) * this.cubicRegionSize;

    // Vanilla's name for a chunk's external file, by its absolute coordinates, beside the region file.
    private string ExternalPath(int chunkX, int chunkZ)
    {
        var (regionX, regionZ) = this.regionPosition!.Value;
        var x = regionX * this.cubicRegionSize + (chunkX & this.op);
        var z = regionZ * this.cubicRegionSize + (chunkZ & this.op);

        return Path.Join(Path.GetDirectoryName(this.filePath), $"c.{x}.{z}.mcc");
    }

    private static (int X, int Z)? ParseRegionPosition(string filePath)
    {
        var parts = Path.GetFileName(filePath).Split('.');
        if (parts.Length != 4 || parts[0] != "r" || parts[3] != "mca")
            return null;

        if (!int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x))
            return null;

        if (!int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z))
            return null;

        return (x, z);
    }

    // Vanilla's compression type ids.
    private static byte CompressionType(NbtCompression compression) => compression switch
    {
        NbtCompression.GZip => 1,
        NbtCompression.ZLib => 2,
        _ => 3
    };

    private static int SectorsFor(long length) => (int)((length + SectorSize - 1) / SectorSize);

    public async ValueTask DisposeAsync()
    {
        // Closed under the lock, so a flush waiting for it finds the file closed rather than closing under it. The lock
        // stays undisposed for those waiters.
        await this.headerLock.WaitAsync();
        try
        {
            if (this.disposed)
                return;

            // A failed final flush still closes the file, so it doesn't stay locked, and is reported.
            try
            {
                await this.FlushUnderLockAsync();
            }
            finally
            {
                this.handle.Dispose();
                this.disposed = true;
            }
        }
        finally
        {
            this.headerLock.Release();
        }
    }

    private enum Storage : byte
    {
        Unknown,
        Internal,
        External
    }

    // The sectors of a chunk's previous version, and the chunk whose external file goes with them, or -1.
    private readonly record struct RetiredRun(int Start, int Count, int ExternalOf);

    /// <summary>
    /// A chunk's turn to be written (see <see cref="ReserveWrite"/>). Disposing it lets the next write go.
    /// </summary>
    internal sealed class ChunkWrite(Task turn) : IDisposable
    {
        private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once every write reserved before this one is done.</summary>
        public Task TurnAsync { get; } = turn;

        internal Task Done => this.done.Task;

        public void Dispose() => this.done.TrySetResult();
    }
}
