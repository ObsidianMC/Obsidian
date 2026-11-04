using Obsidian.Entities;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Players in 64-block columns (four chunks across), with the order of the level's player snapshot.
/// Positions stay live while membership and order follow the snapshot.
/// </summary>
internal sealed class PlayerGrid
{
    // Position writes, membership changes and queries share this lock. A VectorD is not an atomic read/write;
    // locking only bucket changes would allow a query to miss a player between their position and bucket writes.
    // One lock across levels also keeps transfers from needing two level locks in opposite orders. Result
    // enumeration and packet sends run outside it: range queries copy their results before returning.
    internal static Lock Gate { get; } = new();

    private const int ColumnSize = 64;
    private readonly Dictionary<(int X, int Z), List<Entry>> columns = [];
    private readonly Dictionary<IPlayer, Entry> entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> unindexed = [];

    private sealed class Entry(IPlayer player, int order, (int X, int Z) column)
    {
        internal IPlayer Player { get; } = player;
        internal int Order { get; } = order;
        internal (int X, int Z) Column { get; set; } = column;
        internal bool Unindexed { get; set; }
    }

    private static (int X, int Z) Column(VectorD position) =>
        ((int)Math.Floor(position.X / ColumnSize), (int)Math.Floor(position.Z / ColumnSize));

    /// <summary>Replaces membership and order from the same array the tick uses.</summary>
    internal void Rebuild(IPlayer[] players)
    {
        lock (Gate)
        {
            foreach (var player in this.entries.Keys)
            {
                if (player is Entity entity && entity.PlayerQueryGrid == this)
                    entity.PlayerQueryGrid = null;
            }

            this.columns.Clear();
            this.entries.Clear();
            this.unindexed.Clear();

            for (var order = 0; order < players.Length; order++)
            {
                var player = players[order];
                var entry = new Entry(player, order, Column(player.Position));
                this.entries.Add(player, entry);
                if (player is Entity entity && CanIndex(player.Position))
                {
                    this.Bucket(entry.Column).Add(entry);
                    entity.PlayerQueryGrid = this;
                }
                else
                {
                    // Other IPlayer implementations have no position hook. Keep checking their live positions,
                    // and retain non-finite coordinates for the original IEEE distance comparisons.
                    this.unindexed.Add(entry);
                    entry.Unindexed = true;
                    if (player is Entity moving)
                        moving.PlayerQueryGrid = this;
                }
            }
        }
    }

    private static bool Finite(VectorD position) =>
        double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z);

    private static bool CanIndex(VectorD position) => Finite(position)
        && Math.Abs(position.X / ColumnSize) < int.MaxValue - 2
        && Math.Abs(position.Z / ColumnSize) < int.MaxValue - 2;

    private static bool TryBounds(VectorD center, double radius,
        out (int X, int Z) min, out (int X, int Z) max)
    {
        // Widen by one representable value so rounding at an exact column boundary cannot omit a match.
        var low = new VectorD(Math.BitDecrement(center.X - radius), 0, Math.BitDecrement(center.Z - radius));
        var high = new VectorD(Math.BitIncrement(center.X + radius), 0, Math.BitIncrement(center.Z + radius));
        min = Column(low);
        max = Column(high);
        return CanIndex(low) && CanIndex(high);
    }

    private List<Entry> Bucket((int X, int Z) column)
    {
        if (!this.columns.TryGetValue(column, out var bucket))
            this.columns.Add(column, bucket = []);
        return bucket;
    }

    /// <summary>Called by the position setter with <see cref="Gate"/> held.</summary>
    internal void Move(IPlayer player, VectorD position)
    {
        if (!this.entries.TryGetValue(player, out var entry))
            return;

        var column = Column(position);
        var canIndex = CanIndex(position);
        if (!entry.Unindexed && column == entry.Column && canIndex)
            return;

        if (entry.Unindexed)
        {
            this.unindexed.Remove(entry);
        }
        else
        {
            var bucket = this.columns[entry.Column];
            bucket.Remove(entry);
            if (bucket.Count == 0)
                this.columns.Remove(entry.Column);
        }

        entry.Column = column;
        entry.Unindexed = !canIndex;
        if (canIndex)
            this.Bucket(column).Add(entry);
        else
            this.unindexed.Add(entry);
    }

    /// <summary>Matches the horizontal, float-squared range query, preserving snapshot order.</summary>
    internal IPlayer[] InRange(VectorD center, float squaredDistance,
        ConcurrentDictionary<Guid, IPlayer> membership)
    {
        lock (Gate)
        {
            List<Entry>? found = null;
            // A positive float radius can square to zero. The original double distance calculation can also
            // underflow to zero for tiny offsets on either side of a column boundary, so include that rounding bin.
            var radius = Math.Sqrt(Math.BitIncrement((double)squaredDistance));
            var bounded = TryBounds(center, radius, out var min, out var max);

            // A broad query (including float.MaxValue, whose square is infinity) must not walk billions of
            // empty columns. Only bounded, finite coordinates use the rectangle of nearby columns.
            var width = (double)max.X - min.X + 1;
            var depth = (double)max.Z - min.Z + 1;
            if (bounded && double.IsFinite(radius) && width > 0 && depth > 0
                && width * depth <= this.columns.Count * 4 + 16)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    for (var z = min.Z; z <= max.Z; z++)
                    {
                        if (this.columns.TryGetValue((x, z), out var bucket))
                            Check(bucket);
                    }
                }
            }
            else
            {
                foreach (var bucket in this.columns.Values)
                    Check(bucket);
            }

            Check(this.unindexed);
            if (found is null)
                return [];

            found.Sort(static (left, right) => left.Order.CompareTo(right.Order));
            var result = new IPlayer[found.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = found[index].Player;
            return result;

            void Check(List<Entry> bucket)
            {
                foreach (var entry in bucket)
                {
                    var player = entry.Player;
                    var difference = player.Position - center;
                    if (difference.X * difference.X + difference.Z * difference.Z <= squaredDistance
                        && membership.TryGetValue(player.Uuid, out var current) && ReferenceEquals(current, player))
                    {
                        (found ??= []).Add(entry);
                    }
                }
            }
        }
    }

    /// <summary>The exact nearest full 3D distance, excluding spectators, without allocating query results.</summary>
    internal double NearestDistanceSquared(VectorD center, ConcurrentDictionary<Guid, IPlayer> membership)
    {
        lock (Gate)
        {
            var nearest = double.MaxValue;
            var column = Column(center);
            if (this.columns.TryGetValue(column, out var own))
                Check(own);

            // Once the current column gives a distance, only columns intersecting its horizontal circle can
            // improve it. Otherwise start with the neighboring columns.
            var radius = nearest < double.MaxValue ? Math.Sqrt(nearest) : ColumnSize;
            var bounded = TryBounds(center, radius, out var min, out var max);
            var area = ((double)max.X - min.X + 1) * ((double)max.Z - min.Z + 1);
            if (bounded && Finite(center) && double.IsFinite(radius) && area > 0
                && area <= this.columns.Count * 4 + 16)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    for (var z = min.Z; z <= max.Z; z++)
                    {
                        if ((x, z) != column && this.columns.TryGetValue((x, z), out var bucket))
                            Check(bucket);
                    }
                }

                // Use the distance to the actual square's edges: rounding to columns usually searched farther
                // than radius, and a nearest player inside that boundary proves no outside column is closer.
                var edge = Math.Min(center.X - (double)min.X * ColumnSize,
                    ((double)max.X + 1) * ColumnSize - center.X);
                edge = Math.Min(edge, Math.Min(center.Z - (double)min.Z * ColumnSize,
                    ((double)max.Z + 1) * ColumnSize - center.Z));
                if (nearest <= edge * edge)
                {
                    Check(this.unindexed);
                    return nearest;
                }
            }

            // Sparse/distant players, or a large vertical separation: skip whole columns by their conservative
            // horizontal lower bound, rather than capping the answer at a despawn threshold.
            foreach (var (key, bucket) in this.columns)
            {
                var dx = Math.Max(0, Math.Max((double)key.X * ColumnSize - center.X,
                    center.X - ((double)key.X + 1) * ColumnSize));
                var dz = Math.Max(0, Math.Max((double)key.Z * ColumnSize - center.Z,
                    center.Z - ((double)key.Z + 1) * ColumnSize));
                if (dx * dx + dz * dz <= nearest || !Finite(center))
                    Check(bucket);
            }

            Check(this.unindexed);
            return nearest;

            void Check(List<Entry> bucket)
            {
                foreach (var entry in bucket)
                {
                    var player = entry.Player;
                    if (player.GameMode != GameMode.Spectator && membership.ContainsKey(player.Uuid))
                        nearest = Math.Min(nearest, (player.Position - center).MagnitudeSquared());
                }
            }
        }
    }
}
