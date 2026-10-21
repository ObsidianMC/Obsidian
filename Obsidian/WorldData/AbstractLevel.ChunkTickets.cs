using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private const int MaximumChunkTickets = 256;
    private const int MaximumTicketChunks = 1024;
    private const int MaximumTicketRadius = 8;

    // Keep the entire square and border inside vanilla's 30-million-block horizontal build limit.
    private const int MaximumChunkCoordinate = 1_874_999;

    // This lock only protects ticket bookkeeping: never acquire a world lock or run loading/plugin code under it.
    // Callers may hold inventory or entity locks. Simulation reads immutable snapshots without acquiring TickLock
    // backwards. A ticket never holds a plugin instance or anything from its collectible assembly.
    private readonly Lock chunkTicketLock = new();
    private readonly HashSet<ChunkTicket> chunkTickets = [];
    private TicketChunks ticketChunks = new([], []);

    private sealed record TicketChunks(HashSet<long> Loaded, HashSet<long> Ticking);

    // An explicit world request survives ticket release, and a dispatched job finishes under its pins. Ticket and player
    // requests are dropped once neither a ticket nor a player's view needs the chunk (see DiscardUnneededGeneration).
    private enum GenerationDemand { Ticket, Player, Retained, Running }

    private sealed class ChunkTicket(
        AbstractLevel level, PluginOwnership? owner, long[] loaded, long[] ticking) : IDisposable
    {
        private AbstractLevel? level = level;

        public PluginOwnership? Owner { get; } = owner;
        public long[] Loaded { get; } = loaded;
        public long[] Ticking { get; } = ticking;

        public void Dispose() => Interlocked.Exchange(ref this.level, null)?.RemoveChunkTicket(this);
    }

    /// <inheritdoc/>
    public IDisposable AddChunkTicket(int chunkX, int chunkZ, int radius = 0, bool ticking = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, MaximumTicketRadius);
        var loadedRadius = radius + (ticking ? 1 : 0);
        var coordinateLimit = MaximumChunkCoordinate - loadedRadius;
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkX, -coordinateLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkX, coordinateLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkZ, -coordinateLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkZ, coordinateLimit);

        var owner = PluginEntities.FindSpawner();
        var ticket = new ChunkTicket(
            this,
            owner?.Ownership,
            TicketSquare(chunkX, chunkZ, loadedRadius),
            ticking ? TicketSquare(chunkX, chunkZ, radius) : []);

        lock (this.chunkTicketLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref this.disposed) != 0, this);
            owner?.ThrowIfClosing();
            if (this.chunkTickets.Count >= MaximumChunkTickets)
                throw new InvalidOperationException("The level's limit of 256 chunk tickets has been reached.");

            var loaded = new HashSet<long>(this.ticketChunks.Loaded);
            loaded.UnionWith(ticket.Loaded);
            if (loaded.Count > MaximumTicketChunks)
                throw new InvalidOperationException("Chunk tickets may retain at most 1024 chunks per level.");

            this.chunkTickets.Add(ticket);
            this.PublishTicketChunks();
        }

        return ticket;
    }

    private static long[] TicketSquare(int centerX, int centerZ, int radius)
    {
        var width = radius * 2 + 1;
        var chunks = new long[width * width];
        var index = 0;
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
                chunks[index++] = NumericsHelper.IntsToLong(x, z);
        }

        return chunks;
    }

    private void RemoveChunkTicket(ChunkTicket ticket)
    {
        lock (this.chunkTicketLock)
        {
            if (this.chunkTickets.Remove(ticket))
                this.PublishTicketChunks();
        }
    }

    /// <summary>Releases every ticket of this plugin load, including handles the plugin didn't dispose.</summary>
    internal void ReleaseChunkTickets(PluginContainer plugin)
    {
        lock (this.chunkTicketLock)
        {
            foreach (var ticket in this.chunkTickets.Where(ticket => ticket.Owner == plugin.Ownership).ToArray())
            {
                // Remove even if another thread already exchanged the handle's level and is waiting on this lock.
                this.chunkTickets.Remove(ticket);
                ticket.Dispose();
            }

            this.PublishTicketChunks();
        }
    }

    private void ClearChunkTickets()
    {
        lock (this.chunkTicketLock)
        {
            var tickets = this.chunkTickets.ToArray();
            this.chunkTickets.Clear();
            this.PublishTicketChunks();
            foreach (var ticket in tickets)
                ticket.Dispose();
        }
    }

    // Called only under chunkTicketLock; published sets are never changed afterwards.
    private void PublishTicketChunks()
    {
        var loaded = new HashSet<long>();
        var ticking = new HashSet<long>();
        foreach (var ticket in this.chunkTickets)
        {
            loaded.UnionWith(ticket.Loaded);
            ticking.UnionWith(ticket.Ticking);
        }

        Volatile.Write(ref this.ticketChunks, new TicketChunks(loaded, ticking));
        lock (this.waitingJobs)
            this.DiscardUnneededGeneration();
    }

    // Called under chunkTicketLock and waitingJobs, so release and dispatch agree on which jobs have started.
    private void DiscardUnneededGeneration(bool moveToWaiting = false)
    {
        var count = this.ChunksToGen.Count;
        for (var index = 0; index < count && this.ChunksToGen.TryDequeue(out var queued); index++)
        {
            if (Discard(queued))
                continue;

            if (moveToWaiting)
                this.waitingJobs.Add(queued);
            else
                this.ChunksToGen.Enqueue(queued);
        }

        this.waitingJobs.RemoveAll(Discard);

        bool Discard(long key)
        {
            if (this.queuedChunks.TryGetValue(key, out var demand)
                && demand is GenerationDemand.Ticket or GenerationDemand.Player
                && !this.IsTicketLoaded(key)
                && !this.IsInPlayerView(key))
            {
                this.queuedChunks.TryRemove(key, out _);
                return true;
            }

            return false;
        }
    }

    // The region calls this under TickLock at publication and completion, including generation's neighbors.
    // Residency must not depend on a later ticket/player loading pass: its demand may already have disappeared.
    private void TrackPublishedChunk(IChunk chunk, bool completing)
    {
        this.Lighting.Track(chunk, completing);
        this.LoadedChunks.Add(NumericsHelper.IntsToLong(chunk.X, chunk.Z));
    }

    private bool IsTicketLoaded(long chunk) => Volatile.Read(ref this.ticketChunks).Loaded.Contains(chunk);

    private bool IsInPlayerView(long chunk)
    {
        NumericsHelper.LongToInts(chunk, out var x, out var z);
        foreach (var player in this.Players.Values)
        {
            if (player is Entities.Player viewer && viewer.WantsChunk(x, z))
                return true;
        }

        return false;
    }

    // Called between ticks; serialize the retirement decision with ticket admission, without holding this lock
    // through the chunk's asynchronous save. A ticket acquired during that save reloads on the management pass.
    private bool TryReleaseUnneededChunk(long chunk)
    {
        lock (this.chunkTicketLock)
        {
            return !this.IsTicketLoaded(chunk)
                && !this.generationPins.ContainsKey(chunk)
                && this.LoadedChunks.TryRemove(chunk);
        }
    }

    private bool IsTicketTicking(int chunkX, int chunkZ) =>
        Volatile.Read(ref this.ticketChunks).Ticking.Contains(NumericsHelper.IntsToLong(chunkX, chunkZ));

    private bool HasRegionTickets(int regionX, int regionZ) =>
        Volatile.Read(ref this.ticketChunks).Loaded.Any(key =>
        {
            NumericsHelper.LongToInts(key, out var x, out var z);
            return x >> Region.CubicRegionSizeShift == regionX && z >> Region.CubicRegionSizeShift == regionZ;
        });

    // Loading is asynchronous: use the existing queue and generation concurrency limit, never load while holding
    // TickLock. Released tickets don't cancel generation already in flight; its ordinary pins protect those jobs.
    private async Task LoadTicketChunksAsync()
    {
        foreach (var key in Volatile.Read(ref this.ticketChunks).Loaded)
        {
            if (!this.IsTicketLoaded(key))
                continue;

            NumericsHelper.LongToInts(key, out var x, out var z);
            // Residency is already recorded at publication; refresh scheduled ticks when generation is complete.
            await this.GetChunkAsync(x, z, scheduleGeneration: true, ticketOnly: true);
        }
    }
}
