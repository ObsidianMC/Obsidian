using Obsidian.Nbt;
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

    // Like vanilla's portal ticket (TicketType.PORTAL, placed at radius 3 for 300 ticks): for 300 of the level's ticks
    // after an entity came through, the chunks within 3 of the exit stay loaded, those within 2 are block-ticking and
    // those within 1 entity-ticking. A ticket's ticking square is Obsidian's entity-ticking tier, which IsBlockTicking
    // widens by one ring for block ticks, so it has radius 1.
    private const int PortalTicketRadius = 3;
    private const int PortalTicketTickingRadius = 1;
    private const int PortalTicketTicks = 300;

    // How vanilla saves its portal tickets in the level's chunk_tickets saved data: the type, and the ticket level of
    // one placed at radius 3 (ChunkLevel's full-chunk level 33, less the radius). Vanilla persists them (TicketType's
    // FLAG_PERSIST), each with the ticks it has left.
    private const string PortalTicketType = "minecraft:portal";
    private const int PortalTicketLevel = 30;

    // The portal tickets by exit chunk, with the level ticks each has left. Guarded by chunkTicketLock. They're apart
    // from chunkTickets, so that they never count against plugins' ticket limits.
    private readonly Dictionary<long, PortalTicket> portalTickets = [];

    private sealed class PortalTicket(long[] loaded, long[] ticking, int ticksLeft)
    {
        public long[] Loaded { get; } = loaded;
        public long[] Ticking { get; } = ticking;
        public int TicksLeft { get; set; } = ticksLeft;
    }

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

            // Plugins' tickets alone count against the limits, not the level's own portal tickets.
            var loaded = new HashSet<long>(this.chunkTickets.SelectMany(held => held.Loaded));
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
            this.portalTickets.Clear();
            this.PublishTicketChunks();
            foreach (var ticket in tickets)
                ticket.Dispose();
        }
    }

    /// <summary>
    /// Keeps the chunks around <paramref name="anchor"/> loaded and ticking for a while after an entity came through a
    /// portal, like vanilla's portal ticket (see <see cref="PortalTicketRadius"/>). Another one in the same chunk starts
    /// its time again, as vanilla's does.
    /// </summary>
    internal void AddPortalTicket(VectorD anchor)
    {
        var (x, z) = Region.ChunkOf(anchor);
        lock (this.chunkTicketLock)
        {
            if (Volatile.Read(ref this.disposed) != 0)
                return;

            if (this.portalTickets.TryGetValue(NumericsHelper.IntsToLong(x, z), out var existing))
            {
                existing.TicksLeft = PortalTicketTicks;
                return;
            }

            this.AddPortalTicketLocked(x, z, PortalTicketTicks);
            this.PublishTicketChunks();
        }
    }

    /// <summary>
    /// The level ticks the portal ticket in chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>) has left, or
    /// <c>null</c> when there's none there.
    /// </summary>
    internal int? PortalTicketTicksLeft(int chunkX, int chunkZ)
    {
        lock (this.chunkTicketLock)
        {
            return this.portalTickets.TryGetValue(NumericsHelper.IntsToLong(chunkX, chunkZ), out var ticket)
                ? ticket.TicksLeft
                : null;
        }
    }

    // Called under chunkTicketLock; the caller publishes the ticket chunks.
    private void AddPortalTicketLocked(int chunkX, int chunkZ, int ticksLeft)
    {
        var loaded = TicketSquare(chunkX, chunkZ, PortalTicketRadius);
        var ticking = TicketSquare(chunkX, chunkZ, PortalTicketTickingRadius);
        this.portalTickets[NumericsHelper.IntsToLong(chunkX, chunkZ)] = new PortalTicket(loaded, ticking, ticksLeft);
    }

    /// <summary>
    /// Called once a level tick, before the level's blocks and entities tick, as vanilla's chunk source ages its
    /// tickets: releases the portal tickets whose time is up.
    /// </summary>
    internal void AgePortalTickets()
    {
        lock (this.chunkTicketLock)
        {
            if (this.portalTickets.Count == 0)
                return;

            // Like vanilla's Ticket, one lasts while it has 0 ticks left, and goes once it's below that.
            var expired = 0;
            foreach (var (key, ticket) in this.portalTickets)
            {
                if (--ticket.TicksLeft >= 0)
                    continue;

                this.portalTickets.Remove(key);
                expired++;
            }

            if (expired > 0)
                this.PublishTicketChunks();
        }
    }

    // Restores the portal tickets of vanilla's chunk_tickets saved data (see PortalTicketType); the level saves them
    // there with those it doesn't model, which it keeps as they were read (see WriteChunkTickets).
    private void ReadPortalTickets(NbtCompound data)
    {
        if (!data.TryGetTag<NbtList>("tickets", out var tickets))
            return;

        lock (this.chunkTicketLock)
        {
            foreach (var tag in tickets)
            {
                if (!IsPortalTicket(tag)
                    || tag is not NbtCompound ticket
                    || !ticket.TryGetTag<NbtArray<int>>("chunk_pos", out var position)
                    || position.Count != 2)
                {
                    continue;
                }

                // Vanilla leaves out the ticks left when there are none.
                var ticksLeft = ticket.TryGetTagValue<long>("ticks_left", out var saved) ? saved : 0;
                if (ticksLeft < 0)
                    continue;

                // A ticket whose square would reach past the coordinates chunks can have (a damaged or edited save) is
                // dropped, as a plugin's would be refused.
                var chunk = position.GetArray();
                var coordinateLimit = MaximumChunkCoordinate - PortalTicketRadius;
                if (Math.Abs((long)chunk[0]) > coordinateLimit || Math.Abs((long)chunk[1]) > coordinateLimit)
                    continue;

                var restored = (int)Math.Min(ticksLeft, PortalTicketTicks);
                var key = NumericsHelper.IntsToLong(chunk[0], chunk[1]);
                if (this.portalTickets.TryGetValue(key, out var existing) && existing.TicksLeft >= restored)
                    continue;

                this.AddPortalTicketLocked(chunk[0], chunk[1], restored);
            }

            this.PublishTicketChunks();
        }
    }

    // Writes vanilla's chunk_tickets saved data: the tickets it held when it was read, but for the portal tickets,
    // which are the level's own now. A level that never had the file, or any portal ticket, writes none.
    private void WriteChunkTickets()
    {
        var portals = new List<NbtCompound>();
        lock (this.chunkTicketLock)
        {
            foreach (var (key, ticket) in this.portalTickets)
            {
                NumericsHelper.LongToInts(key, out var x, out var z);
                portals.Add(PortalTicketNbt(x, z, ticket.TicksLeft));
            }
        }

        if (this.vanillaChunkTickets is null && portals.Count == 0)
            return;

        var data = this.vanillaChunkTickets ??= new NbtCompound("data");
        var tickets = new NbtList(NbtTagType.Compound, "tickets");
        if (data.TryGetTag<NbtList>("tickets", out var read))
        {
            foreach (var tag in read)
            {
                if (!IsPortalTicket(tag))
                    tickets.Add(tag);
            }
        }

        foreach (var portal in portals)
            tickets.Add(portal);

        // Like vanilla, which leaves the list out when it's empty.
        data.Remove("tickets");
        if (tickets.Count > 0)
            data.Add(tickets);

        SavedDataFile.Write(WorldLayout.SavedDataPath(this.FolderPath, ChunkTicketsDataId), data);
    }

    private static bool IsPortalTicket(INbtTag tag) =>
        tag is NbtCompound ticket && ticket.TryGetTagValue<string>("type", out var type) && type == PortalTicketType;

    // A portal ticket as vanilla's Ticket and TicketStorage codecs save it, which leave out ticks_left when it's 0.
    private static NbtCompound PortalTicketNbt(int chunkX, int chunkZ, int ticksLeft)
    {
        var ticket = new NbtCompound
        {
            new NbtArray<int>("chunk_pos", [chunkX, chunkZ]),
            new NbtTag<string>("type", PortalTicketType),
            new NbtTag<int>("level", PortalTicketLevel)
        };

        if (ticksLeft != 0)
            ticket.Add(new NbtTag<long>("ticks_left", ticksLeft));

        return ticket;
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

        foreach (var ticket in this.portalTickets.Values)
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
        // Taken once, since the pass asks it of every queued chunk.
        Entities.Player[]? viewers = null;

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
                && !IsInView(viewers ??= [.. this.Players.Values.OfType<Entities.Player>()], key))
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

    private static bool IsInView(Entities.Player[] viewers, long chunk)
    {
        NumericsHelper.LongToInts(chunk, out var x, out var z);
        foreach (var viewer in viewers)
        {
            if (viewer.WantsChunk(x, z))
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
