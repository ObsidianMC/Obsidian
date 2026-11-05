using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using System.Threading;

namespace Obsidian.WorldData;

// What keeps the chunk an entity left for another level loaded until the entity is settled (see Entity.Travel.cs),
// and the travellers no level could place yet. All of it is guarded by the level's TransferLock, which the region and
// chunk unloads below take too, so an unload and a departure never both go ahead.
public abstract partial class AbstractLevel
{
    // Chunks entities left and may still come back to or be kept in, with how many such travellers each.
    private readonly Dictionary<long, int> travelPins = [];

    // Chunks chunk management decided to unload, which entities can't leave from until they're unloaded.
    private readonly HashSet<long> closingChunks = [];

    // Travellers neither their destination nor this level could take in or keep yet, retried every tick.
    private readonly List<Entity.Unplaced> unplaced = [];
    private int unplacedRetryScheduled;

    /// <summary>
    /// Pins the chunk at <paramref name="position"/> for an entity leaving it, so neither the chunk nor its region
    /// unloads until the pin is released. Call holding the level's transfer lock.
    /// </summary>
    /// <returns>The pin, or null when the chunk or its region is already on its way out.</returns>
    internal TravelPin? TryPinForTravel(VectorD position)
    {
        var (x, z) = Region.ChunkOf(position);
        var key = NumericsHelper.IntsToLong(x, z);
        if (this.closingChunks.Contains(key) || this.GetRegionForChunk(x, z) is not Region { TransfersClosed: false })
            return null;

        this.travelPins[key] = this.travelPins.GetValueOrDefault(key) + 1;
        return new TravelPin(this, key);
    }

    internal void ReleaseTravelPinFor(long key)
    {
        using (this.TransferLock.Enter())
        {
            if (--this.travelPins[key] == 0)
                this.travelPins.Remove(key);
        }
    }

    /// <summary>
    /// Closes a region to travel before it retires, unless an entity that left it may still come back. Call under the
    /// tick lock, which comes before the transfer lock.
    /// </summary>
    /// <returns>Whether the region may retire.</returns>
    private bool TryCloseRegionToTravel(Region region)
    {
        using (this.TransferLock.Enter())
        {
            foreach (var key in this.travelPins.Keys)
            {
                NumericsHelper.LongToInts(key, out var x, out var z);
                if (x >> Region.CubicRegionSizeShift == region.X && z >> Region.CubicRegionSizeShift == region.Z)
                    return false;
            }

            region.TransfersClosed = true;
            return true;
        }
    }

    /// <summary>
    /// Releases a chunk chunk management doesn't need (see <c>TryReleaseUnneededChunk</c>), unless an entity that left
    /// it may still come back, and closes it to travel until <see cref="ReopenChunkToTravel"/>.
    /// </summary>
    private bool TryReleaseChunkClosedToTravel(long chunk)
    {
        using (this.TransferLock.Enter())
        {
            if (this.travelPins.ContainsKey(chunk) || !this.TryReleaseUnneededChunk(chunk))
                return false;

            this.closingChunks.Add(chunk);
            return true;
        }
    }

    /// <summary>
    /// Whether an entity arriving from another level may join the level at <paramref name="position"/>: not in a region
    /// retiring or a chunk unloading, whose saves have taken or are taking their entities already. Call holding the
    /// level's transfer lock, which those unloads take before they close.
    /// </summary>
    internal bool AdmitsTravellersAt(VectorD position)
    {
        var (x, z) = Region.ChunkOf(position);
        if (this.closingChunks.Contains(NumericsHelper.IntsToLong(x, z)))
            return false;

        if (this.GetRegionForChunk(x, z) is not Region { TransfersClosed: false } region)
            return false;

        var local = region.GetLoadedChunk(
            NumericsHelper.Modulo(x, Region.CubicRegionSize),
            NumericsHelper.Modulo(z, Region.CubicRegionSize));
        return local is Chunk { EntitiesUnloaded: false };
    }

    private void ReopenChunkToTravel(long chunk)
    {
        using (this.TransferLock.Enter())
            this.closingChunks.Remove(chunk);
    }

    /// <summary>
    /// Keeps a traveller no level could take in or keep, retrying it every tick and before each save, which fails
    /// while it's still unplaced rather than going ahead without it.
    /// </summary>
    internal void RetainUnplaced(Entity.Unplaced traveller)
    {
        using (this.TransferLock.Enter())
            this.unplaced.Add(traveller);

        Log.TravellerUnplaced(this.Logger, traveller.Entity.EntityId, this.Name);
        this.ScheduleUnplacedRetry();
    }

    private void ScheduleUnplacedRetry()
    {
        if (Interlocked.Exchange(ref this.unplacedRetryScheduled, 1) != 0)
            return;

        this.EnqueueEntityAction(() =>
        {
            Volatile.Write(ref this.unplacedRetryScheduled, 0);
            if (this.RetryUnplaced() > 0)
                this.ScheduleUnplacedRetry();

            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Tries again to place the travellers kept unplaced.</summary>
    /// <returns>How many are still unplaced.</returns>
    private int RetryUnplaced()
    {
        Entity.Unplaced[] waiting;
        using (this.TransferLock.Enter())
            waiting = [.. this.unplaced];

        // Each stays listed until it's settled; settling one never throws (see Entity.TrySettle).
        foreach (var traveller in waiting)
        {
            if (!traveller.Entity.TrySettle(traveller))
                continue;

            using (this.TransferLock.Enter())
                this.unplaced.Remove(traveller);
        }

        using (this.TransferLock.Enter())
            return this.unplaced.Count;
    }

    private bool HasUnplacedTravellers
    {
        get
        {
            using (this.TransferLock.Enter())
                return this.unplaced.Count > 0;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "Entity {EntityId} could not be placed in {LevelName} or kept in the chunk it left; retrying")]
        public static partial void TravellerUnplaced(ILogger logger, int entityId, string levelName);

        [LoggerMessage(Level = LogLevel.Critical,
            Message = "{Count} travelling entities of {LevelName} could not be placed or kept, and aren't saved")]
        public static partial void TravellersNotSaved(ILogger logger, int count, string levelName);

        [LoggerMessage(Level = LogLevel.Error, Message = "The final save of {LevelName} failed")]
        public static partial void FinalSaveFailed(ILogger logger, Exception exception, string levelName);
    }
}

/// <summary>A hold on the chunk an entity left (see <see cref="AbstractLevel.TryPinForTravel"/>).</summary>
internal sealed class TravelPin
{
    private readonly AbstractLevel level;
    private readonly long key;
    private int released;

    internal TravelPin(AbstractLevel level, long key)
    {
        this.level = level;
        this.key = key;
    }

    internal void Release()
    {
        if (Interlocked.Exchange(ref this.released, 1) == 0)
            this.level.ReleaseTravelPinFor(this.key);
    }
}
