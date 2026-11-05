using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// The final saves of the chunks a level unloaded that aren't committed to its region files yet, by chunk position.
/// An unload takes a chunk's save and makes the chunk unreachable in one step, then writes the save outside the tick;
/// until the write commits, loading the chunk reads it from here, so a reload never sees an older version on disk.
/// </summary>
/// <remarks>
/// Like vanilla's <c>IOWorker</c> pending stores, a later save of a position replaces an earlier one. A write that
/// fails leaves its save here, flagged failed, for the level's next save to retry; a load meanwhile still reads it.
/// The level's regions share one instance, so a region object opened while an earlier one is still closing reads the
/// saves that one is writing.
/// </remarks>
internal sealed class PendingChunkWrites
{
    private readonly Dictionary<(int X, int Z), PendingChunkWrite> pending = [];

    /// <summary>Adds an unloaded chunk's save, in place of any earlier one of its position.</summary>
    internal void Add(PendingChunkWrite write)
    {
        lock (this.pending)
            this.pending[(write.ChunkX, write.ChunkZ)] = write;
    }

    /// <summary>The save pending for the chunk at the position, or null when its files are up to date.</summary>
    internal PendingChunkWrite? Find(int chunkX, int chunkZ)
    {
        lock (this.pending)
            return this.pending.GetValueOrDefault((chunkX, chunkZ));
    }

    /// <summary>Whether <paramref name="write"/> is still the save pending for its position.</summary>
    internal bool IsCurrent(PendingChunkWrite write)
    {
        lock (this.pending)
            return this.pending.TryGetValue((write.ChunkX, write.ChunkZ), out var current) && ReferenceEquals(current, write);
    }

    /// <summary>
    /// Drops <paramref name="write"/>, unless a later save replaced it: once it's committed, or once the chunk was
    /// loaded from it, when the loaded chunk saves itself again.
    /// </summary>
    internal void Remove(PendingChunkWrite write)
    {
        lock (this.pending)
        {
            if (this.pending.TryGetValue((write.ChunkX, write.ChunkZ), out var current) && ReferenceEquals(current, write))
                this.pending.Remove((write.ChunkX, write.ChunkZ));
        }
    }

    /// <summary>The saves whose writes failed, which the next save retries.</summary>
    internal PendingChunkWrite[] Failed()
    {
        lock (this.pending)
            return [.. this.pending.Values.Where(write => write.Failed)];
    }
}

/// <summary>
/// An unloaded chunk's final save (see <see cref="PendingChunkWrites"/>), as uncompressed NBT, which is what the region
/// files hold once decompressed.
/// </summary>
internal sealed class PendingChunkWrite(int chunkX, int chunkZ, byte[]? chunk, byte[]? entities, int entityCount)
{
    internal int ChunkX { get; } = chunkX;

    internal int ChunkZ { get; } = chunkZ;

    /// <summary>
    /// The chunk's NBT, or null when the chunk was unchanged since a save already committed: its file has it.
    /// </summary>
    internal byte[]? Chunk { get; } = chunk;

    /// <summary>The chunk's entity file NBT, or null when the chunk wasn't complete, so it saves no entities.</summary>
    internal byte[]? Entities { get; } = entities;

    internal int EntityCount { get; } = entityCount;

    /// <summary>Whether its last write failed, so the level's next save retries it.</summary>
    internal bool Failed
    {
        get => Volatile.Read(ref this.failed);
        set => Volatile.Write(ref this.failed, value);
    }

    private bool failed;
}
