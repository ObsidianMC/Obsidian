using Obsidian.Entities;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Players in 64-block columns, with live positions and the order of the level's player snapshot.
/// </summary>
internal sealed class PlayerGrid
{
    // Lock order: level membership -> player state -> this grid. Queries only hold the grid while copying
    // candidate references; sorting, whole position reads, filtering and enumeration run outside it.
    // Migration holds the player's state but never two grids: remove from the old grid, release it, then add here.
    internal Lock MembershipGate { get; } = new();
    internal Lock Gate { get; } = new();

    internal sealed class PlayerState
    {
        internal Lock Gate { get; } = new();
        internal PlayerGrid? Grid;
    }

    private const int ColumnSize = 64;
    private readonly Dictionary<(int X, int Z), List<Entry>> columns = [];
    private readonly Dictionary<IPlayer, Entry> entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> unindexed = [];
    private IPlayer[]? snapshotPlayers;
    private long changes;

    private sealed class Entry(IPlayer player, int order, (int X, int Z) column, bool unindexed)
    {
        internal IPlayer Player { get; } = player;
        internal int Order { get; } = order;
        internal (int X, int Z) Column { get; set; } = column;
        internal bool Unindexed { get; set; } = unindexed;
    }

    private readonly record struct Batch(Entry[] Candidates, long Changes, bool Indexed);

    private static PlayerState State(Entity entity) =>
        LazyInitializer.EnsureInitialized(ref entity.PlayerQueryState);

    /// <summary>Reads a whole position without holding any grid's lock.</summary>
    internal static VectorD ReadPosition(IPlayer player)
    {
        if (player is not Entity entity)
            return player.Position;

        lock (State(entity).Gate)
            return player.Position;
    }

    /// <summary>Attaches a player, releasing their previous grid before acquiring this one.</summary>
    internal void Add(IPlayer player)
    {
        if (player is not Entity entity)
        {
            lock (this.Gate)
                this.Insert(player);
            return;
        }

        var state = State(entity);
        lock (state.Gate)
        {
            if (state.Grid is PlayerGrid previous && previous != this)
                previous.Remove(player);

            lock (this.Gate)
            {
                state.Grid = this;
                this.Insert(player);
            }
        }
    }

    private void Insert(IPlayer player)
    {
        if (this.entries.ContainsKey(player))
            return;

        var entry = this.CreateEntry(player, int.MaxValue);
        this.entries.Add(player, entry);
        this.Link(entry);
        this.snapshotPlayers = null;
        this.changes++;
    }

    /// <summary>Detaches a player without acquiring another grid's lock.</summary>
    internal void Remove(IPlayer player)
    {
        if (player is not Entity entity)
        {
            lock (this.Gate)
                this.RemoveEntry(player);
            return;
        }

        var state = State(entity);
        lock (state.Gate)
        {
            lock (this.Gate)
            {
                this.RemoveEntry(player);
                if (state.Grid == this)
                    state.Grid = null;
            }
        }
    }

    private void RemoveEntry(IPlayer player)
    {
        if (!this.entries.Remove(player, out var entry))
            return;

        this.Unlink(entry);
        this.snapshotPlayers = null;
        this.changes++;
    }

    /// <summary>Reconciles public dictionary edits and replaces snapshot order under the membership gate.</summary>
    internal void Rebuild(IPlayer[] players)
    {
        IPlayer[] previous;
        lock (this.Gate)
        {
            previous = this.entries.Keys.ToArray();
            this.snapshotPlayers = null;
            this.changes++;
        }

        var membership = new HashSet<IPlayer>(players, ReferenceEqualityComparer.Instance);
        foreach (var player in previous)
        {
            if (!membership.Contains(player))
                this.Remove(player);
        }
        foreach (var player in players)
            this.Add(player);

        lock (this.Gate)
        {
            this.columns.Clear();
            this.entries.Clear();
            this.unindexed.Clear();
            for (var order = 0; order < players.Length; order++)
            {
                var entry = this.CreateEntry(players[order], order);
                this.entries.Add(entry.Player, entry);
                this.Link(entry);
            }
            this.snapshotPlayers = players;
            this.changes++;
        }
    }

    private Entry CreateEntry(IPlayer player, int order)
    {
        // A player attached elsewhere cannot be read under this grid's gate. Treat it like an implementation
        // without a position hook, and read its position through its own state when filtering outside the gate.
        if (player is Entity entity && Volatile.Read(ref State(entity).Grid) == this)
        {
            var position = player.Position;
            if (CanIndex(position))
                return new Entry(player, order, Column(position), false);
        }
        return new Entry(player, order, default, true);
    }

    private static (int X, int Z) Column(VectorD position) =>
        ((int)Math.Floor(position.X / ColumnSize), (int)Math.Floor(position.Z / ColumnSize));

    private static bool Finite(VectorD position) =>
        double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z);

    private static bool CanIndex(VectorD position) => Finite(position)
        && Math.Abs(position.X / ColumnSize) < int.MaxValue - 2
        && Math.Abs(position.Z / ColumnSize) < int.MaxValue - 2;

    private static bool TryBounds(VectorD center, double radius,
        out (int X, int Z) min, out (int X, int Z) max)
    {
        var low = new VectorD(Math.BitDecrement(center.X - radius), 0, Math.BitDecrement(center.Z - radius));
        var high = new VectorD(Math.BitIncrement(center.X + radius), 0, Math.BitIncrement(center.Z + radius));
        min = Column(low);
        max = Column(high);
        return CanIndex(low) && CanIndex(high);
    }

    private void Link(Entry entry)
    {
        if (entry.Unindexed)
        {
            this.unindexed.Add(entry);
            return;
        }
        if (!this.columns.TryGetValue(entry.Column, out var bucket))
            this.columns.Add(entry.Column, bucket = []);
        bucket.Add(entry);
    }

    private void Unlink(Entry entry)
    {
        if (entry.Unindexed)
        {
            this.unindexed.Remove(entry);
            return;
        }
        var bucket = this.columns[entry.Column];
        bucket.Remove(entry);
        if (bucket.Count == 0)
            this.columns.Remove(entry.Column);
    }

    /// <summary>Called by the position setter with the player's state and this grid's gate held.</summary>
    internal void Move(IPlayer player, VectorD position)
    {
        if (!this.entries.TryGetValue(player, out var entry))
            return;

        var column = Column(position);
        var canIndex = CanIndex(position);
        if (!entry.Unindexed && column == entry.Column && canIndex)
            return;

        this.Unlink(entry);
        entry.Column = column;
        entry.Unindexed = !canIndex;
        this.Link(entry);
        this.changes++;
    }

    /// <summary>Nearby-column candidates in snapshot order; the caller checks live distance and membership.</summary>
    internal IEnumerable<IPlayer> CandidatesInOrder(IPlayer[] snapshot, VectorD center, float squaredDistance)
    {
        var nextOrder = 0;
        while (nextOrder < snapshot.Length)
        {
            var batch = this.CollectInRange(snapshot, center, squaredDistance, nextOrder);
            if (!batch.Indexed)
            {
                // A join, leave or next tick invalidates the grid's order, but an already-started iterator keeps
                // the original snapshot, just like the old array loop. New queries use the dictionary fallback.
                for (; nextOrder < snapshot.Length; nextOrder++)
                    yield return snapshot[nextOrder];
                yield break;
            }

            Array.Sort(batch.Candidates, static (left, right) => left.Order.CompareTo(right.Order));
            var refresh = false;
            foreach (var candidate in batch.Candidates)
            {
                nextOrder = candidate.Order + 1;
                yield return candidate.Player;

                // The old loop finds later players moving into range between MoveNext calls. Collect whole
                // columns without distance filtering; refresh remaining ordinals after column crossings, too.
                if (Volatile.Read(ref this.changes) != batch.Changes)
                {
                    refresh = true;
                    break;
                }
            }
            if (!refresh)
                yield break;
        }
    }

    private Batch CollectInRange(IPlayer[] snapshot, VectorD center, float squaredDistance, int nextOrder = 0)
    {
        // Include the rounding bin when a positive float radius and a tiny double distance both square to zero.
        var radius = Math.Sqrt(Math.BitIncrement((double)squaredDistance));
        var bounded = TryBounds(center, radius, out var min, out var max);
        var area = ((double)max.X - min.X + 1) * ((double)max.Z - min.Z + 1);
        List<Entry>? found = null;
        lock (this.Gate)
        {
            if (!ReferenceEquals(this.snapshotPlayers, snapshot))
                return new Batch([], this.changes, false);

            if (bounded && double.IsFinite(radius) && area > 0 && area <= this.columns.Count * 4 + 16)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    for (var z = min.Z; z <= max.Z; z++)
                    {
                        if (this.columns.TryGetValue((x, z), out var bucket))
                            Collect(bucket);
                    }
                }
            }
            else
            {
                foreach (var bucket in this.columns.Values)
                    Collect(bucket);
            }
            Collect(this.unindexed);
            return new Batch(found?.ToArray() ?? [], this.changes, true);

            void Collect(List<Entry> bucket)
            {
                foreach (var entry in bucket)
                {
                    if (entry.Order >= nextOrder)
                        (found ??= []).Add(entry);
                }
            }
        }
    }

    /// <summary>Exact 3D nearest distance, with all eligibility and distance checks outside the grid's gate.</summary>
    internal double NearestDistanceSquared(IPlayer[] snapshot, VectorD center,
        ConcurrentDictionary<Guid, IPlayer> membership)
    {
        var batch = this.CollectInRange(snapshot, center, ColumnSize * ColumnSize);
        if (!batch.Indexed)
            return NearestFromPlayers(snapshot, center, membership);

        var nearest = NearestFromCandidates(batch.Candidates, center, membership);
        if (Volatile.Read(ref this.changes) != batch.Changes)
            return NearestFromPlayers(snapshot, center, membership);

        if (TryBounds(center, Math.Sqrt(Math.BitIncrement((double)(ColumnSize * ColumnSize))),
            out var min, out var max) && Finite(center))
        {
            var edge = Math.Min(center.X - (double)min.X * ColumnSize,
                ((double)max.X + 1) * ColumnSize - center.X);
            edge = Math.Min(edge, Math.Min(center.Z - (double)min.Z * ColumnSize,
                ((double)max.Z + 1) * ColumnSize - center.Z));
            if (nearest <= edge * edge)
                return nearest;
        }

        List<Entry>? remaining = null;
        lock (this.Gate)
        {
            if (this.changes == batch.Changes)
            {
                // Sparse or vertically distant players: collect only columns whose horizontal lower bound can
                // improve the first batch's answer. Entry order/player references are immutable; positions are
                // read outside the gate, and a column crossing invalidates this bound and uses the fallback.
                foreach (var (key, bucket) in this.columns)
                {
                    var dx = Math.Max(0, Math.Max((double)key.X * ColumnSize - center.X,
                        center.X - ((double)key.X + 1) * ColumnSize));
                    var dz = Math.Max(0, Math.Max((double)key.Z * ColumnSize - center.Z,
                        center.Z - ((double)key.Z + 1) * ColumnSize));
                    if (dx * dx + dz * dz <= nearest || !Finite(center))
                    {
                        foreach (var entry in bucket)
                            (remaining ??= []).Add(entry);
                    }
                }
                foreach (var entry in this.unindexed)
                    (remaining ??= []).Add(entry);
            }
        }
        if (Volatile.Read(ref this.changes) != batch.Changes)
            return NearestFromPlayers(snapshot, center, membership);

        if (remaining is not null)
            nearest = Math.Min(nearest, NearestFromCandidates(remaining, center, membership));
        return Volatile.Read(ref this.changes) == batch.Changes
            ? nearest
            : NearestFromPlayers(snapshot, center, membership);
    }

    private static double NearestFromCandidates(IEnumerable<Entry> candidates, VectorD center,
        ConcurrentDictionary<Guid, IPlayer> membership)
    {
        var nearest = double.MaxValue;
        foreach (var candidate in candidates)
        {
            var player = candidate.Player;
            if (player.GameMode != GameMode.Spectator && membership.ContainsKey(player.Uuid))
            {
                nearest = Math.Min(nearest, (ReadPosition(player) - center).MagnitudeSquared());
            }
        }
        return nearest;
    }

    private static double NearestFromPlayers(IPlayer[] players, VectorD center,
        ConcurrentDictionary<Guid, IPlayer> membership)
    {
        var nearest = double.MaxValue;
        foreach (var player in players)
        {
            if (player.GameMode != GameMode.Spectator && membership.ContainsKey(player.Uuid))
                nearest = Math.Min(nearest, (ReadPosition(player) - center).MagnitudeSquared());
        }
        return nearest;
    }
}
