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

    // Set for good once a player leaves the server (see LeaveServer), under the transfer lock of the level that owns it
    // then. Departure and admission check it under theirs, so whichever comes second sees it: no travel or respawn
    // moves the player on from then on, and no level takes it in. Removing only its registration (a plugin's
    // IServer.RemovePlayer, say) doesn't set it: the player may still be connected and registered again.
    private bool leftServer;

    // Which code may simulate the entity (see TryClaimTick): the level whose tick is ticking it now, and how many travel
    // calls, nested or not, are still moving it or its passengers.
    private AbstractLevel? tickingIn;
    private int travelHolds;

    /// <summary>
    /// Claims the entity for <paramref name="level"/>'s tick of it, which then calls <see cref="ReleaseTick"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule: a level ticks an entity only in its entity phase, only if the entity was among its entities when the
    /// phase began, is still one when its turn comes, no travel call holds it (see <see cref="BeginTravelHold"/>), and no
    /// other level's tick of it is still under way. An entity that changes level belongs to its new level as soon as it
    /// arrives (its membership, lookups and spawn packets, as vanilla adds it at once), but simulation passes to the new
    /// level only once every travel call moving it has returned and the old level's tick of it, if one is still running
    /// (a plugin's tick that awaited the teleport, say), has ended.
    /// </para>
    /// <para>
    /// Like vanilla's entity tick list, one that arrives before the new level's entity phase begins is ticked in it,
    /// and one that arrives after, in the next.
    /// </para>
    /// </remarks>
    /// <returns>Whether the level may tick it now.</returns>
    internal bool TryClaimTick(AbstractLevel level)
    {
        if (Volatile.Read(ref this.travelHolds) != 0)
            return false;

        if (Interlocked.CompareExchange(ref this.tickingIn, level, null) is not null)
            return false;

        // A travel that began meanwhile keeps it.
        if (Volatile.Read(ref this.travelHolds) == 0)
            return true;

        Volatile.Write(ref this.tickingIn, null);
        return false;
    }

    /// <summary>Ends <paramref name="level"/>'s tick of the entity, claimed by <see cref="TryClaimTick"/>.</summary>
    internal void ReleaseTick(AbstractLevel level) => Interlocked.CompareExchange(ref this.tickingIn, null, level);

    /// <summary>
    /// Keeps every level from ticking the entity until the matching <see cref="EndTravelHold"/>: travel takes it before
    /// the entity leaves its level and lets go once it and its passengers are settled. Holds nest.
    /// </summary>
    internal void BeginTravelHold() => Interlocked.Increment(ref this.travelHolds);

    internal void EndTravelHold() => Interlocked.Decrement(ref this.travelHolds);

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
    /// Takes <paramref name="player"/> out of the players of the level that owns it, under that level's transfer lock,
    /// so that a level change can't move it on between reading its level and removing it there. Only the registration
    /// goes: a travel or respawn later registers the player in its new level as usual. For a player leaving the server
    /// for good, use <see cref="LeaveServer"/>.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>Whether the player was among the level's players.</returns>
    /// <exception cref="InvalidOperationException">The current thread holds another level's transfer lock.</exception>
    internal static bool RemoveFromOwningLevel(IPlayer player) => RemoveFromOwningLevel(player, leaving: false);

    /// <summary>
    /// Takes <paramref name="player"/>, which is leaving the server for good (its client disconnected or left, or its
    /// join was cancelled), out of the players of the level that owns it, like <see cref="RemoveFromOwningLevel"/>. The
    /// player stays gone: no travel, respawn or arrival still under way (see <see cref="Arrive"/>) or begun later puts it
    /// in any level's players or entities again.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>Whether the player was among the level's players.</returns>
    /// <exception cref="InvalidOperationException">The current thread holds another level's transfer lock.</exception>
    internal static bool LeaveServer(IPlayer player) => RemoveFromOwningLevel(player, leaving: true);

    private static bool RemoveFromOwningLevel(IPlayer player, bool leaving)
    {
        if (player is not Entity entity)
            return player.Level.TryRemovePlayer(player);

        using (entity.EnterOwningLevel(out var level))
        {
            if (leaving)
                entity.leftServer = true;

            return level.TryRemovePlayer(player);
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
