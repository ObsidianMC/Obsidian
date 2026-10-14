using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.WorldData.Lighting;

/// <summary>
/// Batches live block changes under the level's fluid lock, removing dependent light before spreading sources again.
/// Only complete, tracked chunks participate; loading a neighbor reconciles both sides of their shared border.
/// </summary>
internal sealed class RuntimeLightEngine(Lock tickLock, Func<bool> hasSkyLight)
{
    private static readonly Vector[] steps =
        [Vector.Down, Vector.Up, Vector.North, Vector.South, Vector.West, Vector.East];

    private readonly Dictionary<(int X, int Z), Chunk> chunks = [];
    private readonly Dictionary<(int X, int Z), int[]> skySources = [];
    private readonly HashSet<Vector> changes = [];
    private readonly HashSet<Vector> columns = [];
    private readonly HashSet<Vector> borders = [];
    private readonly Dictionary<(Chunk Chunk, LightType Type, int Section), byte[]> originals = [];
    private readonly HashSet<Chunk> forcedUpdates = [];

    internal Lock TickLock => tickLock;

    /// <summary>Attaches the block-write hook once, and queues reconciliation with loaded neighbors.</summary>
    public void Track(IChunk chunk)
    {
        if (chunk is not Chunk { IsGenerated: true } complete)
            return;

        lock (tickLock)
        {
            var key = (complete.X, complete.Z);
            if (this.chunks.TryGetValue(key, out var previous) && ReferenceEquals(previous, complete))
                return;

            if (previous is not null)
                previous.RuntimeLight = null;

            this.chunks[key] = complete;
            complete.RuntimeLight = this;
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
            for (var direction = 2; direction < 6; direction++)
            {
                var step = steps[direction];
                if (!this.chunks.ContainsKey((complete.X + step.X, complete.Z + step.Z)))
                    continue;

                for (var y = complete.MinY; y < complete.MinY + complete.Height; y++)
                {
                    for (var i = 0; i < 16; i++)
                    {
                        var x = direction < 4 ? i : direction == 4 ? 0 : 15;
                        var z = direction >= 4 ? i : direction == 2 ? 0 : 15;
                        var position = new Vector((complete.X << 4) + x, y, (complete.Z << 4) + z);
                        this.borders.Add(position);
                        this.borders.Add(position + step);
                    }
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
            if (this.chunks.TryGetValue(key, out var stored) && ReferenceEquals(stored, chunk))
            {
                stored.RuntimeLight = null;
                this.chunks.Remove(key);
                this.skySources.Remove(key);
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
                this.forcedUpdates.Add(complete);
        }
    }

    /// <summary>
    /// Resolves pending changes without sending packets. Saves call this under the same lock before their snapshot;
    /// the tick also calls it before random ticks so saplings see the current light.
    /// </summary>
    public void Flush()
    {
        lock (tickLock)
        {
            if (this.changes.Count == 0 && this.borders.Count == 0)
                return;

            var blockSeeds = new HashSet<Vector>(this.changes);
            blockSeeds.UnionWith(this.borders);
            var skySeeds = new HashSet<Vector>(blockSeeds);
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

            this.Relight(blockSeeds, LightType.Block);
            if (hasSkyLight())
                this.Relight(skySeeds, LightType.Sky);

            this.changes.Clear();
            this.columns.Clear();
            this.borders.Clear();
        }
    }

    /// <summary>
    /// Returns immutable packets for sections whose final light changed, at most one per chunk per tick.
    /// </summary>
    public IReadOnlyList<LightUpdatePacket> TakeUpdates()
    {
        lock (tickLock)
        {
            this.Flush();
            var sections = new Dictionary<Chunk, (HashSet<int> Sky, HashSet<int> Block)>();
            foreach (var (key, original) in this.originals)
            {
                // A save/unload/reload can replace the instance between flushes in this tick. Compare against the
                // chunk now loaded at that coordinate, and send it once rather than sending stale storage too.
                var chunk = this.chunks.GetValueOrDefault((key.Chunk.X, key.Chunk.Z));
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
    }

    private void Relight(HashSet<Vector> seeds, LightType type)
    {
        var decreases = new Queue<(Vector Position, int Level)>();
        var restore = new HashSet<Vector>(seeds);
        foreach (var position in seeds)
        {
            if (this.ChunkAt(position) is not Chunk chunk || !Inside(chunk, position))
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
                if (this.ChunkAt(position) is not Chunk chunk || !Inside(chunk, position))
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
            if (this.ChunkAt(position) is not Chunk chunk || !Inside(chunk, position))
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
                if (this.ChunkAt(position) is not Chunk chunk || !Inside(chunk, position))
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

        var source = this.skySources[(chunk.X, chunk.Z)][(position.Z & 15) * 16 + (position.X & 15)];
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
        var key = (chunk, type, sectionIndex);
        if (!this.originals.ContainsKey(key))
        {
            var section = chunk.Sections[sectionIndex];
            this.originals[key] = (type == LightType.Sky ? section.SkyLightArray : section.BlockLightArray).ToArray();
        }

        chunk.SetLightLevel(position.X, position.Y, position.Z, type, level);
    }

    private Chunk? ChunkAt(Vector position) => this.chunks.GetValueOrDefault((position.X >> 4, position.Z >> 4));

    private static bool Inside(Chunk chunk, Vector position) =>
        position.Y >= chunk.MinY && position.Y < chunk.MinY + chunk.Height;
}
