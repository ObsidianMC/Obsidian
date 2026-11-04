using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Entities;

// Which level's transfer lock guards an entity, and the state of an entity between levels (see EntityTransferLock).
public partial class Entity
{
    // Guards entities of levels that aren't Obsidian's own, which have no transfer lock of their own.
    private static readonly EntityTransferLock detachedTransferLock = new();

    // The level the entity left its last level for and hasn't been taken in by yet, and whether it was removed
    // meanwhile. Both change under that level's transfer lock (see Depart and Arrive in Entity.Travel.cs).
    private ILevel? arrivingAt;
    private bool removedOnArrival;

    // The level the entity last arrived in and the tick of it under way then (0 between ticks), which that level
    // doesn't tick the entity in; and whether a travel call is still using the entity (see TeleportAsync).
    private AbstractLevel? arrivedIn;
    private long arrivedDuringTick;
    private volatile bool travelling;

    /// <summary>
    /// Whether <paramref name="level"/>'s tick may tick the entity now. One that arrived from another level isn't ticked
    /// by its new level during the tick under way when it arrived, which the level it left may be running alongside,
    /// nor while the call that moved it is still moving it or its passengers: from then on, only its new level does.
    /// </summary>
    internal bool IsTickableIn(AbstractLevel level)
    {
        if (this.travelling)
            return false;

        return !ReferenceEquals(Volatile.Read(ref this.arrivedIn), level)
            || level.CurrentTickId != Volatile.Read(ref this.arrivedDuringTick);
    }

    // Called by the level taking the entity in, before publishing it.
    private void HoldFromTicking(ILevel level)
    {
        if (level is not AbstractLevel arriving)
            return;

        Volatile.Write(ref this.arrivedDuringTick, arriving.CurrentTickId);
        Volatile.Write(ref this.arrivedIn, arriving);
    }

    /// <summary>
    /// Whether the entity, this instance, is among the entities of <paramref name="level"/>'s regions: removal and
    /// unloading take it out without changing its level. Call holding the level's transfer lock.
    /// </summary>
    internal bool IsMemberOf(ILevel level) =>
        level is AbstractLevel owned
        && owned.GetRegionForLocation(this.Position) is IRegion region
        && region.Entities.TryGetValue(this.EntityId, out var resident)
        && ReferenceEquals(resident, this);

    /// <summary>The transfer lock guarding the entities of <paramref name="level"/>.</summary>
    internal static EntityTransferLock TransferLockOf(ILevel level) =>
        level is AbstractLevel owned ? owned.TransferLock : detachedTransferLock;

    /// <summary>
    /// Enters the transfer lock of the level that owns the entity, the one its <c>Level</c> names, and keeps it until
    /// the scope is disposed. The entity can't change level meanwhile: <c>Level</c> only changes under the lock of the
    /// level it names, so this checks it again once in, and follows the entity if it left first.
    /// </summary>
    /// <param name="level">The level that owns the entity while the scope lasts.</param>
    /// <exception cref="InvalidOperationException">
    /// The current thread holds another level's transfer lock (see <see cref="EntityTransferLock.Enter"/>).
    /// </exception>
    internal EntityTransferLock.Scope EnterOwningLevel(out ILevel level)
    {
        while (true)
        {
            var candidate = this.Level;
            var scope = TransferLockOf(candidate).Enter();
            if (ReferenceEquals(this.Level, candidate))
            {
                level = candidate;
                return scope;
            }

            scope.Dispose();
        }
    }

    /// <summary>
    /// Removes the entity while it travels to <paramref name="level"/>, which hasn't taken it in yet: it won't be.
    /// Call holding the level's transfer lock.
    /// </summary>
    /// <returns>Whether the entity was on its way to the level.</returns>
    internal bool TryRemoveOnArrival(ILevel level)
    {
        if (!ReferenceEquals(this.arrivingAt, level))
            return false;

        this.removedOnArrival = true;
        return true;
    }
}
