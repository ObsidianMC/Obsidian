using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.WorldData.Lighting;

/// <summary>
/// Batches live block changes under the level's fluid lock, removing dependent light before spreading sources again.
/// Only complete, tracked chunks participate; loading a neighbor reconciles both sides of their shared border.
/// </summary>
/// <remarks>
/// Border reconciliation relights only the border blocks whose stored light disagrees with their neighbors, so
/// attaching chunks whose light is already consistent costs a scan, not a relight. Completion skips the scan only when
/// generation lit the resident instance under overlapping-area locks and no runtime relight missed it meanwhile.
/// </remarks>
internal sealed class RuntimeLightEngine(Lock tickLock, Func<bool> hasSkyLight)
{
    private static readonly Vector[] steps =
        [Vector.Down, Vector.Up, Vector.North, Vector.South, Vector.West, Vector.East];

    private readonly Dictionary<(int X, int Z), Chunk> chunks = [];
    private readonly Dictionary<(int X, int Z), int[]> skySources = [];
    // The last chunk Source read sky sources for, cleared whenever tracking replaces or drops a chunk's sources.
    private Chunk? sourcesChunk;
    private int[]? sourcesCache;
    private readonly HashSet<Vector> changes = [];
    private readonly HashSet<Vector> columns = [];
    // Shared borders to reconcile, as the chunk tracked last and the direction (2 to 5) of its tracked neighbor.
    private readonly HashSet<((int X, int Z) Chunk, int Direction)> borders = [];

    // Completion markers belong only to resident prototypes, and retire with their instances. Missing coordinates
    // never acquire markers; a later chunk must either run locked generation lighting or reconcile its borders.
    private readonly Dictionary<(int X, int Z), (Chunk Chunk, bool Lit, bool Missed)> generating = [];
    private readonly Dictionary<(int X, int Z, LightType Type, int Section), byte[]> originals = [];
    private readonly HashSet<(int X, int Z)> forcedUpdates = [];

    // Generation lights chunks next to tracked ones on its own threads, apart from each other by generation's chunk
    // locks. They share this gate's read side; relighting and packet snapshots take its write side.
    private readonly ReaderWriterLockSlim propagation = new();

    internal Lock TickLock => tickLock;

    /// <summary>
    /// Captures an initial chunk packet's immutable light data before queueing it. The propagation gate excludes
    /// both runtime relighting and generation writes; no tick lock is needed, including during serialization.
    /// </summary>
    internal LightUpdatePacket SnapshotLight(IChunk chunk)
    {
        this.propagation.EnterWriteLock();
        try
        {
            return new LightUpdatePacket(chunk);
        }
        finally
        {
            this.propagation.ExitWriteLock();
        }
    }

    /// <summary>
    /// Remembers a resident prototype until completion, or attaches the complete chunk's block-write hook and queues
    /// border reconciliation. The region may prepare the hook just before publishing generation's full status, while
    /// holding the tick lock and validating the slot identity.
    /// </summary>
    public void Track(IChunk chunk, bool completing = false)
    {
        if (chunk is not Chunk complete)
            return;

        lock (tickLock)
        {
            var key = (complete.X, complete.Z);
            if (!complete.IsGenerated && !completing)
            {
                if (!this.generating.TryGetValue(key, out var pending) || !ReferenceEquals(pending.Chunk, complete))
                    this.generating[key] = (complete, false, false);

                return;
            }

            if (this.chunks.TryGetValue(key, out var previous) && ReferenceEquals(previous, complete))
                return;

            if (previous is not null)
                previous.AttachRuntimeLight(null);

            this.chunks[key] = complete;
            complete.AttachRuntimeLight(this);
            var sources = new int[256];
            if (hasSkyLight())
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var x = 0; x < 16; x++)
                        sources[z * 16 + x] = SkyLightSources.LowestSourceY(complete, x, z);
                }
            }

            this.skySources[key] = sources;
            this.sourcesChunk = null;

            // Saved prototypes may resume after their light stage, and legacy generators don't hold overlapping-area
            // locks. Neither can skip reconciliation merely because they reached full status.
            var freshlyLit = this.generating.Remove(key, out var generation)
                && ReferenceEquals(generation.Chunk, complete)
                && generation.Lit
                && !generation.Missed;
            if (!completing || !freshlyLit)
            {
                for (var direction = 2; direction < 6; direction++)
                {
                    var step = steps[direction];
                    if (this.chunks.ContainsKey((complete.X + step.X, complete.Z + step.Z)))
                        this.borders.Add((key, direction));
                }
            }
        }
    }

    /// <summary>Forgets an unloaded chunk after its pending light has been flushed into its save.</summary>
    public void Untrack(IChunk chunk)
    {
        lock (tickLock)
        {
            var key = (chunk.X, chunk.Z);
            if (this.generating.TryGetValue(key, out var pending) && ReferenceEquals(pending.Chunk, chunk))
                this.generating.Remove(key);

            if (this.chunks.TryGetValue(key, out var stored) && ReferenceEquals(stored, chunk))
            {
                stored.AttachRuntimeLight(null);
                this.chunks.Remove(key);
                this.skySources.Remove(key);
                this.sourcesChunk = null;
            }
        }
    }

    /// <summary>Called before a tracked chunk writes a block, while holding <see cref="TickLock"/>.</summary>
    internal void BlockChanging(Chunk chunk, Vector position, IBlock block)
    {
        if (chunk.GetBlock(position.X, position.Y, position.Z).IsSameState(block))
            return;

        this.changes.Add(position);
        if (hasSkyLight())
            this.columns.Add(new Vector(position.X, 0, position.Z));
    }

    /// <summary>Queues generation's changed light for the same per-tick packet batch.</summary>
    public void QueueChunkUpdate(IChunk chunk)
    {
        if (chunk is Chunk complete)
        {
            lock (tickLock)
                this.forcedUpdates.Add((complete.X, complete.Z));
        }
    }

    /// <summary>
    /// Runs generation lighting, which may write lit neighbors. Only callers holding overlapping-area generation
    /// locks may run concurrently; other callers also take the tick lock to exclude save snapshots.
    /// </summary>
    /// <remarks>
    /// <paramref name="light"/> must not take <see cref="TickLock"/>: a flush holds it while it waits for generation.
    /// With <paramref name="hasGenerationLocks"/>, the caller must hold every chunk's lock in the lighting area.
    /// </remarks>
    public void RunGenerationLighting(IChunk chunk, Action light, bool hasGenerationLocks)
    {
        if (!hasGenerationLocks)
        {
            lock (tickLock)
            {
                this.propagation.EnterWriteLock();
                try
                {
                    light();
                }
                finally
                {
                    this.propagation.ExitWriteLock();
                }
            }

            return;
        }

        this.propagation.EnterReadLock();
        try
        {
            light();
        }
        finally
        {
            this.propagation.ExitReadLock();
        }

        // Take the tick lock only after releasing the gate, matching flush's tick-lock-then-gate order. Keep a missed
        // marker even if it predates this lighting pass: an untracked prototype couldn't consume that runtime update.
        lock (tickLock)
        {
            var key = (chunk.X, chunk.Z);
            if (this.generating.TryGetValue(key, out var pending) && ReferenceEquals(pending.Chunk, chunk))
                this.generating[key] = (pending.Chunk, true, pending.Missed);
        }
    }

    /// <summary>
    /// Resolves pending changes without sending packets. Saves call this under the same lock before their snapshot;
    /// the tick also calls it before random ticks so saplings see the current light.
    /// </summary>
    public void Flush() => this.Flush(null);

    /// <summary>
    /// Like <see cref="Flush()"/>, but stops reconciling borders once <paramref name="borderBudget"/> has passed and
    /// leaves the rest for later, so a tick that loads many chunks at once doesn't stall the server. Block changes are
    /// always resolved.
    /// </summary>
    public void Flush(TimeSpan? borderBudget)
    {
        lock (tickLock)
        {
            if (this.changes.Count == 0 && this.borders.Count == 0)
                return;

            this.propagation.EnterWriteLock();
            try
            {
                this.FlushPending(borderBudget);
            }
            finally
            {
                this.propagation.ExitWriteLock();
            }
        }
    }

    private void FlushPending(TimeSpan? borderBudget)
    {
        var blockSeeds = new HashSet<Vector>(this.changes);
        var skySeeds = new HashSet<Vector>(this.changes);
        var blockSpreads = new HashSet<Vector>();
        var skySpreads = new HashSet<Vector>();
        if (hasSkyLight())
        {
            foreach (var column in this.columns)
            {
                if (this.ChunkAt(column) is not Chunk chunk)
                    continue;

                var sources = this.skySources[(chunk.X, chunk.Z)];
                var index = (column.Z & 15) * 16 + (column.X & 15);
                var oldSource = sources[index];
                var newSource = SkyLightSources.LowestSourceY(chunk, column.X & 15, column.Z & 15);
                sources[index] = newSource;
                var start = Math.Max(chunk.MinY, Math.Min(oldSource, newSource));
                var end = Math.Min(chunk.MinY + chunk.Height, Math.Max(oldSource, newSource));
                for (var y = start; y < end; y++)
                    skySeeds.Add(new Vector(column.X, y, column.Z));
            }
        }

        // After the sky sources above are current, so borders compare against what the columns now get.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var reconciled = new List<((int X, int Z) Chunk, int Direction)>();
        foreach (var border in this.borders)
        {
            // A zero budget reconciles no borders; any other makes progress on at least one.
            if (borderBudget is { } budget && (budget == TimeSpan.Zero || reconciled.Count > 0)
                && System.Diagnostics.Stopwatch.GetElapsedTime(started) > budget)
            {
                break;
            }

            this.AddUnsettledBorder(border.Chunk, border.Direction, LightType.Block, blockSeeds, blockSpreads);
            if (hasSkyLight())
                this.AddUnsettledBorder(border.Chunk, border.Direction, LightType.Sky, skySeeds, skySpreads);
            reconciled.Add(border);
        }

        this.Relight(blockSeeds, LightType.Block, blockSpreads);
        if (hasSkyLight())
            this.Relight(skySeeds, LightType.Sky, skySpreads);

        this.changes.Clear();
        this.columns.Clear();
        if (reconciled.Count == this.borders.Count)
            this.borders.Clear();
        else
            this.borders.ExceptWith(reconciled);
    }

    /// <summary>
    /// Returns immutable packets for sections whose final light changed, at most one per chunk per tick.
    /// </summary>
    public IReadOnlyList<LightUpdatePacket> TakeUpdates(TimeSpan? borderBudget = null)
    {
        lock (tickLock)
        {
            this.Flush(borderBudget);
            if (this.originals.Count == 0 && this.forcedUpdates.Count == 0)
                return [];

            this.propagation.EnterWriteLock();
            try
            {
                return this.SnapshotUpdates();
            }
            finally
            {
                this.propagation.ExitWriteLock();
            }
        }
    }

    private LightUpdatePacket[] SnapshotUpdates()
    {
        var sections = new Dictionary<Chunk, (HashSet<int> Sky, HashSet<int> Block)>();
        foreach (var (key, original) in this.originals)
        {
            // A save/unload/reload can replace the instance between flushes in this tick. Compare against the
            // chunk now loaded at that coordinate, and send it once rather than sending stale storage too.
            var chunk = this.chunks.GetValueOrDefault((key.X, key.Z));
            if (chunk is null)
                continue;

            var section = chunk.Sections[key.Section];
            var current = key.Type == LightType.Sky ? section.SkyLightArray : section.BlockLightArray;
            if (original.AsSpan().SequenceEqual(current.Span))
                continue;

            if (!sections.TryGetValue(chunk, out var changed))
                sections[chunk] = changed = ([], []);

            (key.Type == LightType.Sky ? changed.Sky : changed.Block).Add(key.Section);
        }

        foreach (var queued in this.forcedUpdates)
        {
            var chunk = this.chunks.GetValueOrDefault((queued.X, queued.Z));
            if (chunk is null)
                continue;

            var all = Enumerable.Range(0, chunk.Sections.Length);
            sections[chunk] = (all.ToHashSet(), all.ToHashSet());
        }

        var packets = sections.Select(entry =>
            new LightUpdatePacket(entry.Key, entry.Value.Sky, entry.Value.Block)).ToArray();
        this.originals.Clear();
        this.forcedUpdates.Clear();
        return packets;
    }

    /// <remarks>
    /// <paramref name="spreads"/> are blocks that only lack light their neighbors give: rather than removing their light
    /// like <paramref name="seeds"/>, which ripples through all the light around them, their neighbors spread again.
    /// </remarks>
    private void Relight(HashSet<Vector> seeds, LightType type, HashSet<Vector> spreads)
    {
        var decreases = new Queue<(Vector Position, int Level)>();
        var restore = new HashSet<Vector>(seeds);
        foreach (var position in spreads)
        {
            if (seeds.Contains(position))
                continue;

            restore.Add(position);
            foreach (var step in steps)
                restore.Add(position + step);
        }

        foreach (var position in seeds)
        {
            if (this.Reach(position) is not Chunk chunk || !Inside(chunk, position))
                continue;

            var level = chunk.GetLightLevel(position.X, position.Y, position.Z, type);
            if (level > 0)
            {
                this.SetLight(chunk, position, type, 0);
                decreases.Enqueue((position, level));
            }

            foreach (var step in steps)
                restore.Add(position + step);
        }

        // Vanilla's decrease rule removes neighbors below the removed level, independent of their new shape.
        // Independent sources and brighter surviving boundaries are restored only after the decrease queue drains.
        while (decreases.TryDequeue(out var removed))
        {
            foreach (var step in steps)
            {
                var position = removed.Position + step;
                if (this.Reach(position) is not Chunk chunk || !Inside(chunk, position))
                    continue;

                restore.Add(position);
                var level = chunk.GetLightLevel(position.X, position.Y, position.Z, type);
                if (level > 0 && level < removed.Level)
                {
                    this.SetLight(chunk, position, type, 0);
                    decreases.Enqueue((position, level));
                }
            }
        }

        var increases = new Queue<(Vector Position, int Level)>();
        foreach (var position in restore)
        {
            if (this.Reach(position) is not Chunk chunk || !Inside(chunk, position))
                continue;

            var stored = chunk.GetLightLevel(position.X, position.Y, position.Z, type);
            var source = this.Source(chunk, position, type);
            if (source > stored)
            {
                this.SetLight(chunk, position, type, source);
                stored = source;
            }

            if (stored > 1)
                increases.Enqueue((position, stored));
        }

        while (increases.TryDequeue(out var entry))
        {
            var fromChunk = this.ChunkAt(entry.Position)!;
            if (fromChunk.GetLightLevel(entry.Position.X, entry.Position.Y, entry.Position.Z, type) != entry.Level)
                continue;

            var from = fromChunk.GetBlock(entry.Position.X, entry.Position.Y, entry.Position.Z);
            for (var direction = 0; direction < 6; direction++)
            {
                var position = entry.Position + steps[direction];
                if (this.ReachNear(fromChunk, position) is not Chunk chunk || !Inside(chunk, position))
                    continue;

                var stored = chunk.GetLightLevel(position.X, position.Y, position.Z, type);
                if (entry.Level - 1 <= stored)
                    continue;

                var block = chunk.GetBlock(position.X, position.Y, position.Z);
                var nextLevel = entry.Level - Math.Max(1, block.LightBlock());
                if (nextLevel <= stored || BlockLight.LightShapesOcclude(from, block, (BlockFace)direction))
                    continue;

                this.SetLight(chunk, position, type, nextLevel);
                if (nextLevel > 1)
                    increases.Enqueue((position, nextLevel));
            }
        }
    }

    private int Source(Chunk chunk, Vector position, LightType type)
    {
        if (type == LightType.Block)
            return chunk.GetBlock(position.X, position.Y, position.Z).LightEmission();

        if (!ReferenceEquals(chunk, this.sourcesChunk))
        {
            this.sourcesCache = this.skySources[(chunk.X, chunk.Z)];
            this.sourcesChunk = chunk;
        }

        var source = this.sourcesCache![(position.Z & 15) * 16 + (position.X & 15)];
        if (position.Y >= source)
            return 15;

        // Matches generation's virtual source in vanilla's section above the build range.
        if (position.Y == chunk.MinY + chunk.Height - 1)
        {
            var block = chunk.GetBlock(position.X, position.Y, position.Z);
            if (!BlockLight.LightShapesOcclude(BlocksRegistry.Air, block, BlockFace.Down))
                return Math.Max(0, 15 - Math.Max(1, block.LightBlock()));
        }

        return 0;
    }

    private void SetLight(Chunk chunk, Vector position, LightType type, int level)
    {
        var sectionIndex = (position.Y - chunk.MinY) >> 4;
        // Coordinates preserve a save's pending packet across reloads without retaining unloaded chunk instances.
        var key = (chunk.X, chunk.Z, type, sectionIndex);
        if (!this.originals.ContainsKey(key))
        {
            var section = chunk.Sections[sectionIndex];
            this.originals[key] = (type == LightType.Sky ? section.SkyLightArray : section.BlockLightArray).ToArray();
        }

        chunk.SetLightLevel(position.X, position.Y, position.Z, type, level);
    }

    private Chunk? ChunkAt(Vector position) => this.chunks.GetValueOrDefault((position.X >> 4, position.Z >> 4));

    // A neighbor's chunk, skipping the lookup when it's the same column as a chunk in hand: almost always, per block.
    private Chunk? ChunkNear(Chunk chunk, Vector position) =>
        position.X >> 4 == chunk.X && position.Z >> 4 == chunk.Z ? chunk : this.ChunkAt(position);

    private Chunk? ReachNear(Chunk chunk, Vector position) =>
        position.X >> 4 == chunk.X && position.Z >> 4 == chunk.Z ? chunk : this.Reach(position);

    // A relight's lookup: an untracked chunk it reaches keeps light that may disagree once that chunk is tracked.
    private Chunk? Reach(Vector position)
    {
        var chunk = this.ChunkAt(position);
        var key = (position.X >> 4, position.Z >> 4);
        if (chunk is null && this.generating.TryGetValue(key, out var pending))
            this.generating[key] = (pending.Chunk, pending.Lit, true);

        return chunk;
    }

    /// <summary>
    /// Adds the blocks on both sides of a shared border whose stored light isn't what their own source and their
    /// neighbors give them. Both chunks were settled on their own before they met, so only these can disagree.
    /// </summary>
    private void AddUnsettledBorder((int X, int Z) key, int direction, LightType type, HashSet<Vector> seeds,
        HashSet<Vector> spreads)
    {
        var step = steps[direction];
        if (this.chunks.GetValueOrDefault(key) is not Chunk chunk
            || this.chunks.GetValueOrDefault((key.X + step.X, key.Z + step.Z)) is not Chunk neighbor)
        {
            return;
        }

        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y++)
        {
            // Whole 16-tall slices that can't disagree are skipped, which is most of a border.
            if (((y - chunk.MinY) & 15) == 0 && this.IsUniformSlice(chunk, neighbor, (y - chunk.MinY) >> 4, direction, type))
            {
                y += 15;
                continue;
            }

            for (var i = 0; i < 16; i++)
            {
                var x = direction < 4 ? i : direction == 4 ? 0 : 15;
                var z = direction >= 4 ? i : direction == 2 ? 0 : 15;
                var position = new Vector((chunk.X << 4) + x, y, (chunk.Z << 4) + z);
                Add(this.Settle(chunk, position, type), position);
                Add(this.Settle(neighbor, position + step, type), position + step);
            }
        }

        void Add(Unsettled state, Vector position)
        {
            if (state == Unsettled.Excess)
                seeds.Add(position);
            else if (state == Unsettled.Missing)
                spreads.Add(position);
        }
    }

    private enum Unsettled { No, Missing, Excess }

    /// <summary>
    /// Whether a section-tall slice of a border is settled without looking at its blocks: both sides dark, or, for sky
    /// light, both sides at full light with every border column's sky source at or below the slice.
    /// </summary>
    /// <remarks>
    /// Each chunk was settled on its own, so only light crossing the border can disagree. Dark sides send none across
    /// and need none; full sky light is its own source on both sides, so nothing crossing can raise or support it.
    /// </remarks>
    private bool IsUniformSlice(Chunk chunk, Chunk neighbor, int section, int direction, LightType type)
    {
        var own = LightArray(chunk, section, type);
        var other = LightArray(neighbor, section, type);
        if (own.IndexOfAnyExcept((byte)0) < 0 && other.IndexOfAnyExcept((byte)0) < 0)
            return true;

        if (type != LightType.Sky || own.Length != 2048 || other.Length != 2048
            || own.IndexOfAnyExcept((byte)0xFF) >= 0 || other.IndexOfAnyExcept((byte)0xFF) >= 0)
        {
            return false;
        }

        var bottom = chunk.MinY + (section << 4);
        var ownSources = this.skySources[(chunk.X, chunk.Z)];
        var otherSources = this.skySources[(neighbor.X, neighbor.Z)];
        for (var i = 0; i < 16; i++)
        {
            var x = direction < 4 ? i : direction == 4 ? 0 : 15;
            var z = direction >= 4 ? i : direction == 2 ? 0 : 15;
            // The neighbor's column across the border.
            var otherX = direction == 4 ? 15 : direction == 5 ? 0 : x;
            var otherZ = direction == 2 ? 15 : direction == 3 ? 0 : z;
            if (ownSources[z * 16 + x] > bottom || otherSources[otherZ * 16 + otherX] > bottom)
                return false;
        }

        return true;
    }

    private static ReadOnlySpan<byte> LightArray(Chunk chunk, int section, LightType type) =>
        (type == LightType.Sky ? chunk.Sections[section].SkyLightArray : chunk.Sections[section].BlockLightArray).Span;

    /// <summary>
    /// How a block's stored light differs from the fixed point of <see cref="Relight"/>'s rules: the higher of its own
    /// source and the brightest unoccluded neighbor's light less the block's attenuation.
    /// </summary>
    /// <remarks>
    /// The light of a neighbor in an untracked chunk is unknown, so it may support the block's light. Missing light is
    /// still caught there, and so is excess light whose support is known to be gone.
    /// </remarks>
    private Unsettled Settle(Chunk chunk, Vector position, LightType type)
    {
        var stored = chunk.GetLightLevel(position.X, position.Y, position.Z, type);
        var source = this.Source(chunk, position, type);
        if (source > stored)
            return Unsettled.Missing;

        var block = chunk.GetBlock(position.X, position.Y, position.Z);
        var attenuation = Math.Max(1, block.LightBlock());
        var supported = stored <= source;
        // No neighbor's light (at most 15) less this block's attenuation can then exceed what it stores.
        if (supported && stored + attenuation >= 15)
            return Unsettled.No;

        for (var direction = 0; direction < 6; direction++)
        {
            var from = position + steps[direction];
            if (!Inside(chunk, from))
                continue;

            if (this.ChunkNear(chunk, from) is not Chunk fromChunk)
            {
                supported = true;
                continue;
            }

            var level = fromChunk.GetLightLevel(from.X, from.Y, from.Z, type) - attenuation;
            if (level < stored || (level == stored && supported))
                continue;

            // That neighbor's light travels the opposite way, toward this block.
            var fromBlock = fromChunk.GetBlock(from.X, from.Y, from.Z);
            if (BlockLight.LightShapesOcclude(fromBlock, block, (BlockFace)(direction ^ 1)))
                continue;

            if (level > stored)
                return Unsettled.Missing;

            supported = true;
        }

        return supported ? Unsettled.No : Unsettled.Excess;
    }

    private static bool Inside(Chunk chunk, Vector position) =>
        position.Y >= chunk.MinY && position.Y < chunk.MinY + chunk.Height;
}
