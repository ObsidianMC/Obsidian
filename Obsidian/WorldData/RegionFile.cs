using Microsoft.Win32.SafeHandles;
using Obsidian.Nbt;
using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A region file in vanilla's Anvil format: a header of 1024 chunk locations and 1024 timestamps, then the chunks, each
/// in a run of 4 KiB sectors starting with its length and compression type. Chunks too big for 255 sectors are kept in
/// an external <c>c.x.z.mcc</c> file beside the region file, like vanilla's.
/// </summary>
/// <remarks>
/// <para>
/// Writes are staged, then committed in batches. Staging writes a chunk's new version to free sectors, never over its
/// previous version. A commit (see <see cref="CommitAsync"/>) then flushes the file, so every staged payload and the
/// file growth it made are on the disk; writes the staged chunks' header entries, switching them to their new
/// versions; flushes again; and only then frees the previous versions' sectors, once no read may still be reading
/// them. An autosave stages all its chunks and commits once: two flushes per file however many chunks it writes.
/// </para>
/// <para>
/// On a disk that keeps what a flush reported written, this guarantees that no header entry on the disk points at a
/// payload that isn't, and that no previous version's sectors are reused before the entry replacing it is on the
/// disk. It doesn't guarantee that writing a 4-byte entry is all or nothing: entries are aligned and never cross a
/// sector, which disks write whole in practice, but no API promises it. A torn entry would leave that one chunk's
/// entry invalid, with both versions' payloads intact, and no other chunk's.
/// </para>
/// <para>
/// Payloads are read and written at their own offsets, outside the lock, which only guards the header, the sector
/// map and the staged chunks. Writes of a chunk take turns (see <see cref="ReserveWrite"/>): each is staged only once
/// the one before it is committed or given up.
/// </para>
/// </remarks>
public sealed partial class RegionFile : IAsyncDisposable
{
    /// <summary>
    /// The zlib (or gzip) level, 0 to 9, chunk payloads are compressed at. Vanilla reads every level the same. Measured
    /// on a terrain chunk of 120 KB of NBT: level 1 (<see cref="CompressionLevel.Fastest"/>) gives 5,184 bytes in
    /// 43 us, level 2 3,390 bytes in 49 us, and level 6 (the default, <see cref="CompressionLevel.Optimal"/>)
    /// 2,886 bytes in 219 us. Level 2 is nearly as fast as level 1 for files a little bigger than level 6's.
    /// </summary>
    internal const int PayloadCompressionLevel = 2;

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

    private const uint MoveFileReplaceExisting = 0x1;
    private const uint MoveFileWriteThrough = 0x8;

    private static readonly ReadOnlyMemory<byte> zeroes = new byte[SectorSize];

    private readonly string filePath;

    private readonly int cubicRegionSize;
    private readonly int op;

    // The region's position, from the file's name, which names its chunks' external files; null when it has none.
    private readonly (int X, int Z)? regionPosition;

    private readonly SafeFileHandle handle;

    // Guards the header, the sector map, the staged chunks, the retired runs and the file's length. Payload I/O happens
    // outside it.
    private readonly SemaphoreSlim headerLock = new(1, 1);

    // One commit at a time, so the flushes of one batch are never interleaved with another's header writes.
    private readonly SemaphoreSlim commitLock = new(1, 1);

    // Held while external files are replaced and their header entries switched, and while an external chunk's file is
    // read, so a read never pairs a stub with a file of another version.
    private readonly SemaphoreSlim externalLock = new(1, 1);

    // The last write reserved for each chunk, which the next one waits for; guarded by itself.
    private readonly Task?[] lastWrites = new Task?[HeaderTableSize];

    private readonly int[] locations = new int[HeaderTableSize];
    private readonly int[] timestamps = new int[HeaderTableSize];

    // Whether each chunk's committed version is external, as far as this session knows.
    private readonly Storage[] storage = new Storage[HeaderTableSize];

    // How many header entries (or staged chunks) use each of the first sectorCount sectors; past them every sector is
    // free. A damaged file can have entries sharing sectors: a sector is free only once none of them uses it.
    private int[] sectorUsers = [];
    private int sectorCount;

    // Payloads written to free sectors, waiting for the next commit.
    private List<StagedChunk> staged = [];

    // Runs of previous versions whose switch is on the disk, kept until the reads that may still be reading them end.
    // Between two commits, the file keeps both versions of every chunk rewritten since the last one, so a region whose
    // chunks all change between autosaves can grow to about twice its chunks' size before reusing the old runs.
    private readonly List<RetiredRun> retiredBehindReads = [];

    private int activeReads;

    private long fileLength;

    private bool disposed;
    private bool initialized;

    /// <summary>
    /// The length of the file, as far as the writes that completed made it.
    /// </summary>
    public long EndOfFile => Volatile.Read(ref this.fileLength);

    public NbtCompression Compression { get; }

    /// <summary>
    /// How many chunks share sectors with another chunk, as found when the file was opened. Their sectors are kept
    /// until no chunk uses them.
    /// </summary>
    internal int OverlappingChunks { get; private set; }

    /// <summary>
    /// Called at each step of staging and committing writes. Tests record the steps, or fail or pause a write at one.
    /// </summary>
    internal Func<Event, ValueTask>? Observe { get; set; }

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
    /// Writes the provided chunk data to the region file, and commits it.
    /// </summary>
    /// <param name="chunkX">The chunks local x coordinate.</param>
    /// <param name="chunkZ">The chunks local z coordinate</param>
    /// <param name="chunkData">The serialized nbt data, compressed in <see cref="Compression"/>.</param>
    public async Task SetChunkAsync(int chunkX, int chunkZ, Memory<byte> chunkData)
    {
        var write = await this.StageAsync(chunkX, chunkZ, chunkData);

        await this.CommitAsync();
        await write.Done;

        if (write.Failure is Exception failure)
            ExceptionDispatchInfo.Throw(failure);
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
    /// Stages a chunk's compressed payload in its turn, for the next commit (see <see cref="CommitAsync"/>), like a
    /// batch of writes that commits once.
    /// </summary>
    internal async Task<ChunkWrite> StageAsync(int chunkX, int chunkZ, ReadOnlyMemory<byte> payload)
    {
        var write = this.ReserveWrite(chunkX, chunkZ);
        try
        {
            await this.StageAsync(write, payload);
            return write;
        }
        finally
        {
            // Only gives the write up when it wasn't staged.
            write.Dispose();
        }
    }

    /// <summary>
    /// Takes the chunk's next turn to be written: the write waits for <see cref="ChunkWrite.TurnAsync"/>, which
    /// completes once every write reserved before it is committed or given up. Reserving writes as their contents are
    /// taken writes them in that order, however long each takes to compress. A reservation that isn't staged must be
    /// given up (disposed); the commit of a staged one ends it.
    /// </summary>
    internal ChunkWrite ReserveWrite(int chunkX, int chunkZ)
    {
        var tableIndex = this.GetChunkTableIndex(chunkX, chunkZ);

        lock (this.lastWrites)
        {
            var write = new ChunkWrite(tableIndex, this.lastWrites[tableIndex] ?? Task.CompletedTask);
            this.lastWrites[tableIndex] = write.Done;

            return write;
        }
    }

    /// <summary>
    /// Compresses a chunk's NBT as this file stores it, at <see cref="PayloadCompressionLevel"/>. Return the result to
    /// the pool once written.
    /// </summary>
    internal PooledBufferStream Compress(ReadOnlySpan<byte> nbt) => CompressAs(CompressionType(this.Compression), nbt);

    /// <summary>
    /// Writes a chunk's compressed payload to free sectors, in the write's turn, and adds it to the next commit. Until
    /// then reads still find the chunk's previous version.
    /// </summary>
    internal async Task StageAsync(ChunkWrite write, ReadOnlyMemory<byte> payload)
    {
        await write.TurnAsync;

        var tableIndex = write.TableIndex;
        var (chunkX, chunkZ) = this.PositionOf(tableIndex);
        var compressionType = CompressionType(this.Compression);
        var sectorsNeeded = SectorsFor(PrefixSize + payload.Length);
        PooledBufferStream? transcoded = null;
        string? externalTemporary = null;
        var start = -1;
        try
        {
            // Like vanilla, a chunk too big for its 255 sectors goes to its external file, and the region keeps a
            // stub (length 1, the compression type flagged external) in one sector. The file is written to a
            // temporary and flushed, then moved over the previous one as the write commits.
            var external = sectorsNeeded > MaxInternalSectors;
            if (external)
            {
                (compressionType, transcoded) = await this.KeepExternalCodecAsync(tableIndex, compressionType, payload);
                var externalPayload = transcoded?.Written ?? payload;
                externalTemporary = await this.WriteExternalTemporaryAsync(chunkX, chunkZ, externalPayload);
                compressionType |= ExternalFlag;
                payload = ReadOnlyMemory<byte>.Empty;
                sectorsNeeded = 1;
            }

            bool extendsFile;
            await this.headerLock.WaitAsync();
            try
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);

                (start, extendsFile) = this.Allocate(sectorsNeeded);
            }
            finally
            {
                this.headerLock.Release();
            }

            await this.WritePayloadAsync(start, sectorsNeeded, extendsFile, compressionType, payload);
            await this.ObserveAsync(Step.PayloadWritten, tableIndex);

            await this.headerLock.WaitAsync();
            try
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);

                this.fileLength = Math.Max(this.fileLength, (long)(start + sectorsNeeded) * SectorSize);
                this.staged.Add(new StagedChunk(write, start, sectorsNeeded, external, externalTemporary));
                write.MarkStaged();
            }
            finally
            {
                this.headerLock.Release();
            }
        }
        catch
        {
            // No header entry ever pointed at the sectors.
            if (start >= 0)
                await this.FreeAsync(start, sectorsNeeded);

            if (externalTemporary is not null)
                File.Delete(externalTemporary);

            throw;
        }
        finally
        {
            transcoded?.Return();
        }
    }

    /// <summary>
    /// Commits every staged write: flushes the file, writes their header entries, flushes again, then frees their
    /// previous versions' sectors and ends their turns. A failure ends the turns of the writes it failed, with it as
    /// their <see cref="ChunkWrite.Failure"/>.
    /// </summary>
    internal async Task CommitAsync()
    {
        await this.commitLock.WaitAsync();
        try
        {
            await this.CommitUnderLockAsync();
        }
        finally
        {
            this.commitLock.Release();
        }
    }

    private async Task CommitUnderLockAsync()
    {
        List<StagedChunk> batch;
        await this.headerLock.WaitAsync();
        try
        {
            batch = this.staged;
            this.staged = [];
        }
        finally
        {
            this.headerLock.Release();
        }

        if (batch.Count == 0)
            return;

        // The payloads, and the file's growth they made, reach the disk before any entry points at them.
        try
        {
            await this.FlushToDiskAsync();
        }
        catch (Exception exception)
        {
            await this.AbandonAsync(batch, exception);
            throw;
        }

        var retired = new List<RetiredRun>();
        var switched = 0;
        Exception? failure = null;
        await this.externalLock.WaitAsync();
        try
        {
            for (; switched < batch.Count; switched++)
                await this.SwitchAsync(batch[switched], retired);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            this.externalLock.Release();
        }

        // The switches reach the disk before the previous versions' sectors can be reused.
        if (failure is null)
        {
            try
            {
                await this.FlushToDiskAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure is not null)
        {
            await this.FailAsync(batch, switched, failure);
            ExceptionDispatchInfo.Throw(failure);
        }

        await this.headerLock.WaitAsync();
        try
        {
            this.retiredBehindReads.AddRange(retired);
            this.ReleaseRetiredRuns();
        }
        finally
        {
            this.headerLock.Release();
        }

        await this.ObserveAsync(Step.SectorsReleased, -1);

        foreach (var chunk in batch)
            chunk.Write.Release(null);
    }

    // Switches a staged chunk's header entry to its new version. Called under the commit and external locks.
    private async Task SwitchAsync(StagedChunk chunk, List<RetiredRun> retired)
    {
        var tableIndex = chunk.Write.TableIndex;
        var (chunkX, chunkZ) = this.PositionOf(tableIndex);

        // The new external file replaces the previous one, durably, before the entry names its stub. A chunk already
        // external has the same stub either way, so the move itself is its commit point: the file keeps the codec that
        // stub names (see KeepExternalCodecAsync), and a crash after it reads the new version, which is complete.
        if (chunk.ExternalTemporary is string temporary)
        {
            MoveDurably(temporary, this.ExternalPath(chunkX, chunkZ));
            chunk.ExternalTemporary = null;
        }

        await this.ObserveAsync(Step.HeaderWriting, tableIndex);

        var timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var location = (chunk.Start << 8) | chunk.Count;

        var timestampEntry = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(timestampEntry, timestamp);

        var locationEntry = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(locationEntry, location);

        await this.headerLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            // Vanilla only reports timestamps. Only the chunk's own entries are written, not the whole header.
            await RandomAccess.WriteAsync(this.handle, timestampEntry, SectorSize + tableIndex * sizeof(int));
            this.timestamps[tableIndex] = timestamp;

            await RandomAccess.WriteAsync(this.handle, locationEntry, tableIndex * sizeof(int));

            // Readers take the location under the lock; its payload is on the disk already.
            var previous = this.locations[tableIndex];
            Volatile.Write(ref this.locations[tableIndex], location);

            var previousStorage = this.storage[tableIndex];
            this.storage[tableIndex] = chunk.External ? Storage.External : Storage.Internal;

            if (previous == 0)
                return;

            // When the previous version was (or may have been) external and this one isn't, its external file goes with
            // its sectors, like vanilla deletes it.
            var deleteExternal = !chunk.External && previousStorage != Storage.Internal
                && this.regionPosition is not null;
            retired.Add(new RetiredRun(previous >> 8, previous & 0xFF, deleteExternal ? tableIndex : -1));
        }
        finally
        {
            this.headerLock.Release();
        }
    }

    // Ends a batch whose payloads never reached the disk: no entry points at their sectors, so they're freed.
    private async Task AbandonAsync(List<StagedChunk> batch, Exception failure)
    {
        foreach (var chunk in batch)
        {
            await this.FreeAsync(chunk.Start, chunk.Count);

            if (chunk.ExternalTemporary is not null)
                File.Delete(chunk.ExternalTemporary);

            chunk.Write.Release(failure);
        }
    }

    // Ends a batch that failed while switching its entries or flushing them. The entries written, and the one being
    // written, may point at either version on the disk, so their sectors stay used this session (the sector map is
    // rebuilt from the header when the file is opened again). Those not reached never pointed at theirs.
    private async Task FailAsync(List<StagedChunk> batch, int switched, Exception failure)
    {
        for (var index = 0; index < batch.Count; index++)
        {
            var chunk = batch[index];
            if (index > switched)
                await this.FreeAsync(chunk.Start, chunk.Count);

            // Still a temporary when it wasn't moved in.
            if (chunk.ExternalTemporary is not null)
                File.Delete(chunk.ExternalTemporary);

            chunk.Write.Release(failure);
        }
    }

    private async Task FreeAsync(int start, int count)
    {
        await this.headerLock.WaitAsync();
        try
        {
            if (!this.disposed)
                this.MarkFree(start, count);
        }
        finally
        {
            this.headerLock.Release();
        }
    }

    // Like vanilla's synced writes, this also has the operating system write its cache to the disk.
    private async Task FlushToDiskAsync()
    {
        await WorkerThreads.Storage.RunAsync(() => RandomAccess.FlushToDisk(this.handle));
        await this.ObserveAsync(Step.Flushed, -1);
    }

    private ValueTask ObserveAsync(Step step, int tableIndex)
    {
        if (this.Observe is null)
            return ValueTask.CompletedTask;

        var (x, z) = tableIndex < 0 ? (-1, -1) : this.PositionOf(tableIndex);
        return this.Observe(new Event(step, x, z));
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
        while (true)
        {
            // The location is taken under the lock, so it's one a commit switched to, never one being switched. Its
            // sectors aren't reused while the read is running (see ReleaseRetiredRuns), so they're read outside it.
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
                if (await this.ReadChunkAsync(tableIndex, offset, size) is RentedArray<byte> chunk)
                    return chunk;
            }
            finally
            {
                await this.headerLock.WaitAsync();
                this.activeReads--;
                this.ReleaseRetiredRuns();
                this.headerLock.Release();
            }

            // An external chunk switched to another version while its stub was read; read the one now current.
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
    /// Commits what was staged (see <see cref="CommitAsync"/>), which puts it on the disk; what was committed already
    /// is there. Waiting for the disk happens on the storage threads (see <see cref="WorkerThreads.Storage"/>), not on
    /// the caller's.
    /// </summary>
    public Task FlushAsync() => this.CommitAsync();

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
                var (x, z) = this.PositionOf(run.ExternalOf);
                DeleteStaleExternal(this.ExternalPath(x, z));
            }
        }

        this.retiredBehindReads.Clear();
    }

    // A stale external file is harmless, since its chunk's header says it's internal, so failing to delete it doesn't
    // fail the read or commit that released it.
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

    /// <summary>
    /// The compression type a new external version is written in, and the payload recompressed in it when that isn't
    /// this file's. Replacing an external chunk's file keeps the codec its current stub names: until the header
    /// switches, that stub may be read with the new file.
    /// </summary>
    private async Task<(byte Type, PooledBufferStream? Transcoded)> KeepExternalCodecAsync(int tableIndex, byte type,
        ReadOnlyMemory<byte> payload)
    {
        // The chunk's current version stays put: it's this write's turn, and its sectors are freed only by this write.
        long offset;
        await this.headerLock.WaitAsync();
        try
        {
            (offset, _) = this.GetLocation(tableIndex);
        }
        finally
        {
            this.headerLock.Release();
        }

        if (offset == 0)
            return (type, null);

        var prefix = new byte[PrefixSize];
        var read = await RandomAccess.ReadAsync(this.handle, prefix, offset);
        if (read < PrefixSize || (prefix[4] & ExternalFlag) == 0)
            return (type, null);

        var current = (byte)(prefix[4] & ~ExternalFlag);
        if (current == type)
            return (type, null);

        var (chunkX, chunkZ) = this.PositionOf(tableIndex);
        using var nbt = this.Decompress(chunkX, chunkZ, type, payload);

        return (current, CompressAs(current, nbt.Span));
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

            // On the disk before it's moved in, so the move never puts in a file a crash can empty.
            file.Flush(flushToDisk: true);
        }

        return temporary;
    }

    // Reads a chunk's sectors and decompresses its payload; null when it's external and switched to another version
    // while its stub was read.
    private async Task<RentedArray<byte>?> ReadChunkAsync(int tableIndex, long offset, int size)
    {
        var (chunkX, chunkZ) = this.PositionOf(tableIndex);
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

        // The file is read under the external lock, after checking the stub read is still the chunk's: no commit
        // replaces the file or switches the entry meanwhile.
        await this.externalLock.WaitAsync();
        try
        {
            await this.headerLock.WaitAsync();
            var current = this.GetLocation(tableIndex) == (offset, size);
            this.headerLock.Release();

            if (!current)
                return null;

            using var external = await ReadExternalAsync(this.ExternalPath(chunkX, chunkZ));
            return this.Decompress(chunkX, chunkZ, (byte)(compressionType & ~ExternalFlag), external.Memory);
        }
        finally
        {
            this.externalLock.Release();
        }
    }

    private static async Task<RentedArray<byte>> ReadExternalAsync(string path)
    {
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

    // Compresses in vanilla's compression type (1 gzip, 2 zlib, 3 none).
    private static PooledBufferStream CompressAs(byte compressionType, ReadOnlySpan<byte> nbt)
    {
        // Chunk NBT compresses several times over, so a quarter of it is a generous start.
        var compressed = new PooledBufferStream(Math.Max(nbt.Length / 4, SectorSize));
        try
        {
            var options = new ZLibCompressionOptions { CompressionLevel = PayloadCompressionLevel };
            using Stream compressor = compressionType switch
            {
                1 => new GZipStream(compressed, options, leaveOpen: true),
                2 => new ZLibStream(compressed, options, leaveOpen: true),
                3 => compressed,
                _ => throw new NotSupportedException($"Chunks can't be written in compression type {compressionType}.")
            };

            compressor.Write(nbt);
            return compressed;
        }
        catch
        {
            compressed.Return();
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
            run = this.sectorUsers[sector] != 0 ? 0 : run + 1;
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
        this.sectorUsers = new int[this.sectorCount];
        this.MarkUsed(0, HeaderSectors);

        for (var index = 0; index < HeaderTableSize; index++)
        {
            var (offset, size) = this.GetLocation(index);
            if (offset == 0 && size == 0)
                continue;

            this.ValidateLocation(index, offset, size);
            this.MarkUsed((int)(offset / SectorSize), size / SectorSize);
        }

        // Counted once every entry is in: a chunk overlaps when any of its sectors has another user.
        for (var index = 0; index < HeaderTableSize; index++)
        {
            var (offset, size) = this.GetLocation(index);
            if (offset == 0 && size == 0)
                continue;

            var users = this.sectorUsers.AsSpan((int)(offset / SectorSize), size / SectorSize);
            if (users.ContainsAnyExcept(1))
                this.OverlappingChunks++;
        }
    }

    private void MarkUsed(int start, int count)
    {
        var end = start + count;
        if (end > this.sectorUsers.Length)
            Array.Resize(ref this.sectorUsers, Math.Max(end, this.sectorUsers.Length * 2));

        this.sectorCount = Math.Max(this.sectorCount, end);
        for (var sector = start; sector < end; sector++)
            this.sectorUsers[sector]++;
    }

    private void MarkFree(int start, int count)
    {
        var end = Math.Min(start + count, this.sectorCount);
        for (var sector = start; sector < end; sector++)
        {
            if (this.sectorUsers[sector] > 0)
                this.sectorUsers[sector]--;
        }
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

        var (x, z) = this.PositionOf(tableIndex);
        throw new InvalidDataException($"Region file '{this.filePath}', chunk ({x}, {z}) has invalid allocation at "
            + $"byte {offset} with length {size}; file length is {this.fileLength}.");
    }

    private int GetChunkTableIndex(int x, int z) =>
        (x & this.op) + (z & this.op) * this.cubicRegionSize;

    private (int X, int Z) PositionOf(int tableIndex) =>
        (tableIndex % this.cubicRegionSize, tableIndex / this.cubicRegionSize);

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

    // Moves a file over another, returning once the move is on the disk: the directory holding the name is flushed too.
    private static void MoveDurably(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(source, destination, MoveFileReplaceExisting | MoveFileWriteThrough))
            {
                var error = new Win32Exception(Marshal.GetLastPInvokeError());
                throw new IOException($"Couldn't move '{source}' to '{destination}'.", error);
            }

            return;
        }

        File.Move(source, destination, overwrite: true);

        var folder = Path.GetDirectoryName(destination)!;
        var directory = OpenDirectory(folder, 0);
        if (directory < 0)
            throw new IOException($"Couldn't open '{folder}' to flush it ({Marshal.GetLastPInvokeError()}).");

        try
        {
            if (FlushDescriptor(directory) != 0)
                throw new IOException($"Couldn't flush '{folder}' ({Marshal.GetLastPInvokeError()}).");
        }
        finally
        {
            _ = CloseDescriptor(directory);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string existing, string replacement, uint flags);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenDirectory(string path, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int FlushDescriptor(int descriptor);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseDescriptor(int descriptor);

    public async ValueTask DisposeAsync()
    {
        // Commits what's staged, then closes under the locks, so a commit or flush waiting for them finds the file
        // closed rather than closing under it. The locks stay undisposed for those waiters.
        await this.commitLock.WaitAsync();
        try
        {
            try
            {
                if (!this.disposed)
                    await this.CommitUnderLockAsync();
            }
            finally
            {
                await this.CloseAsync();
            }
        }
        finally
        {
            this.commitLock.Release();
        }
    }

    private async Task CloseAsync()
    {
        await this.headerLock.WaitAsync();
        try
        {
            if (this.disposed)
                return;

            // A file opened ending within a sector is padded, like vanilla pads it on closing; every write since
            // wrote whole sectors at the end.
            try
            {
                if (this.fileLength % SectorSize != 0)
                    RandomAccess.SetLength(this.handle, SectorsFor(this.fileLength) * (long)SectorSize);
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

    /// <summary>A step of staging and committing writes, and the chunk it's of, or -1 for the whole file.</summary>
    internal readonly record struct Event(Step Step, int ChunkX, int ChunkZ);

    internal enum Step
    {
        // A chunk's payload is in its free sectors.
        PayloadWritten,

        // The file was flushed to disk.
        Flushed,

        // A chunk's header entry is about to be written, after its external file (if any) was moved in.
        HeaderWriting,

        // A commit freed its previous versions' sectors (or kept them for the reads still running).
        SectorsReleased
    }

    private enum Storage : byte
    {
        Unknown,
        Internal,
        External
    }

    // A staged payload, in sectors no entry points at yet, and its external file's temporary until it's moved in.
    private sealed class StagedChunk(ChunkWrite write, int start, int count, bool external, string? externalTemporary)
    {
        public ChunkWrite Write { get; } = write;

        public int Start { get; } = start;

        public int Count { get; } = count;

        public bool External { get; } = external;

        public string? ExternalTemporary { get; set; } = externalTemporary;
    }

    // The sectors of a chunk's previous version, and the chunk whose external file goes with them, or -1.
    private readonly record struct RetiredRun(int Start, int Count, int ExternalOf);

    /// <summary>
    /// A chunk's turn to be written (see <see cref="ReserveWrite"/>). It ends when its write is committed or fails, or
    /// when it's given up (disposed) unstaged, and <see cref="Done"/> then completes once every turn before it ended.
    /// </summary>
    internal sealed class ChunkWrite : IDisposable
    {
        private const int Reserved = 0;
        private const int Staged = 1;
        private const int Ended = 2;

        private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int state;

        internal ChunkWrite(int tableIndex, Task turn)
        {
            this.TableIndex = tableIndex;
            this.TurnAsync = turn;
        }

        internal int TableIndex { get; }

        /// <summary>Completes once every write reserved before this one ended.</summary>
        public Task TurnAsync { get; }

        /// <summary>Completes once this write and every write before it ended; it never fails.</summary>
        public Task Done => this.done.Task;

        /// <summary>
        /// Why the write failed, once <see cref="Done"/> completed; null when it committed or was given up.
        /// </summary>
        public Exception? Failure { get; private set; }

        /// <summary>Gives the write up, unless it was staged: then its commit ends it.</summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref this.state, Ended, Reserved) == Reserved)
                this.End();
        }

        internal void MarkStaged() => Interlocked.CompareExchange(ref this.state, Staged, Reserved);

        internal void Release(Exception? failure)
        {
            if (Interlocked.Exchange(ref this.state, Ended) == Ended)
                return;

            this.Failure = failure;
            this.End();
        }

        // Done waits for the turn before, so giving up a write in the middle never lets a later one overtake.
        private void End() => this.TurnAsync.ContinueWith(_ => this.done.TrySetResult(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
