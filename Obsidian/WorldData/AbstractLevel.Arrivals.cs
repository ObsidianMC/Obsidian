using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.WorldData.Portals;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// An entity on its way into a level from another level's tick, waiting for the level to take it in at its own pace.
/// </summary>
/// <param name="Entity">The entity, which names the level as its own and is in none of its regions yet.</param>
/// <param name="From">Where it left, which keeps its chunk pinned until it's settled somewhere.</param>
/// <param name="Returning">Whether it's on its way back to where it left, the level having refused it.</param>
/// <param name="Area">The destination chunks loaded for it, held until it's taken in.</param>
internal sealed record PendingArrival(Entity Entity, Entity.Departure From, bool Returning, PortalArea? Area);

public abstract partial class AbstractLevel
{
    // Entities that left another level during its tick for this one, oldest first. Guarded by TransferLock: an
    // entity waiting here is the level's (its Level names it), and a removal meanwhile stops it from arriving.
    private readonly List<PendingArrival> arrivals = [];
    private int arrivalsScheduled;

    /// <summary>How many entities are waiting to arrive in the level.</summary>
    internal int PendingArrivalCount
    {
        get
        {
            using (this.TransferLock.Enter())
                return this.arrivals.Count;
        }
    }

    /// <summary>
    /// Queues an entity that left another level during that level's tick, to be taken in with this level's entity
    /// actions, at the start of its next tick, rather than in the middle of whatever this level is doing.
    /// </summary>
    internal void QueueArrival(PendingArrival arrival)
    {
        using (this.TransferLock.Enter())
            this.arrivals.Add(arrival);

        if (Interlocked.Exchange(ref this.arrivalsScheduled, 1) != 0)
            return;

        this.EnqueueEntityAction(() =>
        {
            Volatile.Write(ref this.arrivalsScheduled, 0);
            this.AdoptArrivals();
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Takes in the entities waiting to arrive, in the order they left. Runs with the level's entity actions, and before
    /// a flush, a region unload or disposal saves the level, so a waiting entity is saved with the level it's in.
    /// Callers hold the simulation gate or run after the level's last tick.
    /// </summary>
    internal void AdoptArrivals()
    {
        PendingArrival[] waiting;
        using (this.TransferLock.Enter())
        {
            waiting = [.. this.arrivals];
            this.arrivals.Clear();
        }

        foreach (var arrival in waiting)
            arrival.Entity.Adopt(this, arrival);
    }

    /// <summary>Keeps an arrival the level couldn't settle yet waiting, to try again on its next adoption.</summary>
    internal void RetainArrival(PendingArrival arrival)
    {
        using (this.TransferLock.Enter())
            this.arrivals.Add(arrival);

        Log.ArrivalRetained(this.Logger, arrival.Entity.EntityId, this.Name);
    }

    // Logged when an entity that can't be placed in its destination or source, nor kept in its source chunk, stays
    // waiting: the next flush or tick tries again.
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Entity {EntityId} could not be placed in {LevelName} or kept where it left; it stays waiting")]
        public static partial void ArrivalRetained(ILogger logger, int entityId, string levelName);
    }
}

/// <summary>
/// A hold on a chunk of a level (see <see cref="AbstractLevel.PinPortalChunk"/>) that keeps it loaded until disposed,
/// such as the one an entity left, so it can come back or be kept there.
/// </summary>
internal sealed class ChunkPin : IDisposable
{
    private readonly AbstractLevel level;
    private readonly int x;
    private readonly int z;
    private int released;

    internal ChunkPin(AbstractLevel level, VectorD position)
    {
        this.level = level;
        (this.x, this.z) = Region.ChunkOf(position);
        level.PinPortalChunk(this.x, this.z, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.released, 1) == 0)
            this.level.PinPortalChunk(this.x, this.z, -1);
    }
}
