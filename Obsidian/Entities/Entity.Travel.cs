using Microsoft.Extensions.Logging;
using Obsidian.API.World;
using Obsidian.WorldData;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Portals;

namespace Obsidian.Entities;

// Travel within and between levels. Between levels it's a handoff that never holds both levels' transfer locks: the
// source lets the entity go under its own lock (Depart), and the destination takes it in under its own (Arrive), at
// once, as vanilla adds a teleported entity to its new level straight away. No level ticks it while a travel call is
// moving it or another level's tick of it is still running (see TryClaimTick), so a level ticking in parallel never
// ticks an entity another level's code is still moving. Plugin code (SpawnEntity) only runs once no transfer lock is
// held.
public partial class Entity
{
    /// <summary>Transfers the existing instance, including its state, instead of spawning a replacement.</summary>
    public virtual async ValueTask TeleportAsync(
        ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null)
    {
        ValidateTravel(level, position, yaw, pitch);
        this.CancelEntityPortalTravel();
        var source = this.Level;
        using var area = level is AbstractLevel destination
            ? await PortalArea.LoadAsync(destination, (Vector)position.Floor(), 0, () => this.IsTravelResident(source))
            : null;
        if (level is AbstractLevel && area is null)
            throw new InvalidOperationException("The entity moved while the destination was loading.");

        var held = false;
        try
        {
            Entity[] riders;
            VectorD from;
            Angle fromYaw;
            Angle fromPitch;
            using (TransferLockOf(source).Enter())
            {
                this.ValidateTravelResident(source);

                // Like vanilla, an entity teleported on its own gets off its vehicle, and its passengers get off to
                // travel separately and board again at the destination.
                riders = this.DetachPassengersForTravel();
                from = this.Position;
                fromYaw = this.Yaw;
                fromPitch = this.Pitch;

                // No level ticks the entity from here until its passengers are seated again.
                this.BeginTravelHold();
                held = true;

                // Within the level the move is committed with the detach; travel to another level can't take the
                // destination's lock while this one is held, so it's committed below.
                if (source == level)
                    this.TravelOrReseat(riders, () => this.RelocateWithinLevel(source, position, yaw, pitch));
            }

            if (source != level)
            {
                this.TravelOrReseat(riders, () => this.CommitTravel(source, level, position, yaw, pitch));
                this.AppearIn(level);
            }

            await this.CarryPassengersAsync(riders, from, fromYaw, fromPitch);
        }
        finally
        {
            if (held)
                this.EndTravelHold();
        }
    }

    // Commits a teleport, seating the passengers taken off for it again if it fails.
    private void TravelOrReseat(Entity[] riders, Action travel)
    {
        try
        {
            travel();
        }
        catch (InvalidOperationException)
        {
            foreach (var rider in riders)
                rider.Ride(this, force: true);

            throw;
        }
    }

    // Shown to the players of the level it arrived in, unless it was removed or moved on meanwhile. Spawning is
    // virtual, so plugin code, which may travel itself, and must run without a transfer lock held.
    private void AppearIn(ILevel level)
    {
        if (this.IsTravelResident(level))
            this.SpawnEntity();
    }

    /// <summary>
    /// Moves passengers taken off this entity before it travelled to the same place relative to it, each with its own
    /// passengers, and seats them again. A passenger that can't follow, such as one removed meanwhile, stays behind.
    /// </summary>
    private protected async ValueTask CarryPassengersAsync(
        Entity[] riders, VectorD from, Angle fromYaw, Angle fromPitch)
    {
        foreach (var rider in riders)
        {
            // Vanilla's passenger transition: the same offset from the vehicle, turned as much as the vehicle turned.
            var riderYaw = this.Yaw.Degrees + (rider.Yaw.Degrees - fromYaw.Degrees);
            var riderPitch = this.Pitch.Degrees + (rider.Pitch.Degrees - fromPitch.Degrees);
            try
            {
                await rider.TeleportAsync(this.Level, this.Position + (rider.Position - from), riderYaw, riderPitch);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            rider.Ride(this, force: true);
        }
    }

    /// <summary>Captures and detaches the passengers that follow this entity, including a player vehicle.</summary>
    private protected Entity[] DetachPassengersForTravel()
    {
        using (this.EnterOwningLevel(out _))
        {
            this.LeaveVehicle();
            var riders = this.Passengers.ToArray();
            this.EjectPassengers();
            return riders;
        }
    }

    // Membership is authoritative for ordinary removal and unloading saves; item pickup and mob death may mark
    // removal before their dictionary entry is taken out, and a player's leave before its leave event takes it out.
    // Read and revalidate under the level's transfer lock.
    private protected bool IsTravelResident(ILevel level)
    {
        using (TransferLockOf(level).Enter())
        {
            return this.Level == level
                && !this.leftServer
                && this is not ItemEntity { Removed: true }
                && this is not Mob { IsRemoved: true }
                && level is AbstractLevel resident
                && resident.GetRegionForLocation(this.Position) is IRegion region
                && region.Entities.TryGetValue(this.EntityId, out var entity)
                && ReferenceEquals(entity, this);
        }
    }

    private void ValidateTravelResident(ILevel source)
    {
        if (!this.IsTravelResident(source))
            throw new InvalidOperationException("The entity was removed or moved while the destination was loading.");
    }

    /// <summary>
    /// Moves the entity to <paramref name="position"/> in <paramref name="destination"/> at once. Between levels the
    /// source lets it go (see <see cref="Depart"/>) and the destination takes it in (see <see cref="Arrive"/>); when
    /// the destination can't, it goes back, or, failing that, is kept in the chunk it left (see
    /// <see cref="KeepWhereItLeft"/>). In between it's in neither level, owned by the destination, and a removal
    /// meanwhile (see <see cref="TryRemoveOnArrival"/>) keeps it from arriving.
    /// </summary>
    /// <remarks>
    /// Call holding no transfer lock: travel asked for under one (by code a transfer runs) is refused before the
    /// entity leaves. No awaits, plugin code or tick-lock acquisition here: saves take tick lock, then transfer lock,
    /// then chunk entity lock.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The entity isn't in <paramref name="source"/> anymore, the destination couldn't take it, it was removed on the
    /// way, or a transfer lock is held.
    /// </exception>
    private protected void CommitTravel(
        ILevel source, ILevel destination, VectorD position, Angle? yaw, Angle? pitch)
    {
        if (source == destination)
        {
            this.RelocateWithinLevel(source, position, yaw, pitch);
            return;
        }

        if (source is not AbstractLevel)
            throw new NotSupportedException("Entity transfers require an Obsidian source level.");

        RefuseNestedTravel();
        var departure = this.Depart(source, destination, position, yaw, pitch);

        // From here the entity is in neither level until settled: every way out below either places it, keeps it,
        // or leaves it retained, and only those release the chunk it left.
        var arrival = this.Arrive(destination, departure, out var failure);
        if (arrival == Arrival.Arrived)
        {
            departure.Pin.Release();
            return;
        }

        if (arrival == Arrival.Removed)
        {
            departure.Pin.Release();
            throw new InvalidOperationException("The entity was removed while it travelled.");
        }

        // Never dropped: back where it left, or kept in the chunk it left, or else retried until it is.
        var refused = new Unplaced(this, departure);
        if (!this.TrySettle(refused))
            departure.Level.RetainUnplaced(refused);

        throw new InvalidOperationException("The destination level could not accept the entity.", failure);
    }

    /// <summary>An entity its destination refused, on its way back to where it left.</summary>
    internal readonly record struct Unplaced(Entity Entity, Departure From);

    /// <summary>
    /// Settles an entity its destination refused: back into the level it left, or, when that refuses it too, kept in
    /// the chunk it left (see <see cref="KeepWhereItLeft"/>), which stays pinned until then.
    /// </summary>
    /// <returns>Whether it's settled, or was removed meanwhile.</returns>
    internal bool TrySettle(Unplaced traveller)
    {
        var from = traveller.From;
        try
        {
            if (this.Arrive(from.Level, returning: null, out _) == Arrival.Refused && !this.KeepWhereItLeft(from))
                return false;
        }
        catch (Exception exception)
        {
            // Still unsettled, and still the caller's to retain: nothing above lets it go before it's placed or kept.
            from.Level.Logger.LogError(exception, "Settling travelling entity {EntityId} failed", this.EntityId);
            return false;
        }

        from.Pin.Release();
        return true;
    }

    private static void RefuseNestedTravel()
    {
        if (EntityTransferLock.IsAnyHeldByCurrentThread)
        {
            throw new InvalidOperationException(
                "An entity can't travel to another level from code run under a level's transfer lock.");
        }
    }

    private void RelocateWithinLevel(ILevel level, VectorD position, Angle? yaw, Angle? pitch)
    {
        using (TransferLockOf(level).Enter())
        {
            this.ValidateTravelResident(level);
            var from = this.Position;
            if (level is AbstractLevel resident && !resident.TryMoveEntity(this, from, position))
                throw new InvalidOperationException("The destination level could not accept the entity.");

            this.Place(from, position, yaw ?? this.Yaw, pitch ?? this.Pitch);
            this.PacketBroadcaster.QueuePacketToLevel(level, new TeleportEntityPacket
            {
                EntityId = this.EntityId,
                Position = position,
                Delta = this.Motion,
                Yaw = this.Yaw,
                Pitch = this.Pitch,
                OnGround = this.MovementFlags.HasFlag(MovementFlags.OnGround)
            });
        }
    }

    /// <summary>
    /// Where the entity was, and how it faced, before it left a level, and the pin keeping that chunk and its region
    /// loaded until the entity is settled, so that it can go back or be kept there.
    /// </summary>
    internal readonly record struct Departure(
        AbstractLevel Level, VectorD Position, Angle Yaw, Angle Pitch, TravelPin Pin);

    private enum Arrival
    {
        Arrived,
        Refused,
        Removed
    }

    // The source's half of a handoff, under its transfer lock: the entity leaves its regions, players and riding
    // links, takes its new place, and names the destination as its level, which owns it from then on.
    private Departure Depart(ILevel source, ILevel destination, VectorD position, Angle? yaw, Angle? pitch)
    {
        var origin = (AbstractLevel)source;
        Departure departure;
        using (origin.TransferLock.Enter())
        {
            this.ValidateTravelResident(source);

            // Riding links never span levels: whoever boarded since the caller took the riders off stays behind.
            this.LeaveVehicle();
            this.EjectPassengers();

            var pin = origin.TryPinForTravel(this.Position)
                ?? throw new InvalidOperationException("The chunk the entity is in is unloading.");
            departure = new Departure(origin, this.Position, this.Yaw, this.Pitch, pin);
            this.Leave(source, destination, departure.Position, position, yaw ?? this.Yaw, pitch ?? this.Pitch);
        }

        this.ForgetIn(source);
        return departure;
    }

    // What leaving a level for another, or for a new place in the same one, changes, under the source's transfer lock:
    // the entity leaves its regions (and, between levels, its players), takes its new place, and names the destination
    // as its level, which owns it from then on.
    private void Leave(ILevel source, ILevel destination, VectorD from, VectorD position, Angle yaw, Angle pitch)
    {
        if (source is AbstractLevel origin)
            origin.RemoveEntity(this, transferring: true);

        if (this is IPlayer player && source != destination)
            source.TryRemovePlayer(player);

        this.Place(from, position, yaw, pitch);
        this.removedOnArrival = false;
        this.arrivingAt = destination;
        this.Level = destination;
    }

    // The players of a level the entity left stop tracking it.
    private void ForgetIn(ILevel source)
    {
        foreach (var observer in source.Players.Values)
            observer.UntrackEntity(this.EntityId);
    }

    /// <summary>
    /// Moves a player respawning after death to <paramref name="position"/> in <paramref name="destination"/>, which
    /// may be the level it died in, through travel's handoff: it leaves where it died under that level's transfer lock
    /// and is taken in under the destination's (see <see cref="Arrive"/>), never holding both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="CommitTravel"/>, as a respawn never goes back: the dead player leaves where it died whether or not
    /// it's still there, so no chunk is pinned for a return. Nor does it wait for the destination's chunk: a plugin may
    /// await the respawn inside the destination's tick, which chunk management, and so generation, waits for. When the
    /// destination can't take the player in yet (its chunk is still loading, generating or unloading), the player stays
    /// the destination's, waiting, and the destination takes it in on a later tick (see
    /// <see cref="AbstractLevel.KeepWaitingToArrive"/>).
    /// </para>
    /// <para>
    /// A waiting player is in none of the level's regions and isn't among its players, so nothing simulates it until
    /// it's taken in; a teleport meanwhile takes it in at its new place, or it waits there (see
    /// <see cref="TryMoveWhileArriving"/>).
    /// </para>
    /// <para>
    /// A player that left the server first stays where it left (see <see cref="LeaveServer"/>). Call holding no
    /// transfer lock, with a travel hold (see <see cref="BeginTravelHold"/>) taken until the respawn is over.
    /// </para>
    /// </remarks>
    /// <returns>
    /// False only when the destination, a level that isn't Obsidian's, refused the player, which then isn't among its
    /// entities.
    /// </returns>
    /// <exception cref="InvalidOperationException">A transfer lock is held.</exception>
    private protected bool CommitRespawn(ILevel destination, VectorD position, Angle yaw, Angle pitch)
    {
        RefuseNestedTravel();
        ILevel source;
        Arrival? arrival = null;
        using (this.EnterOwningLevel(out source))
        {
            // Its leave has taken it out and saves it as it left.
            if (this.leftServer)
                return true;

            this.Leave(source, destination, position, position, yaw, pitch);

            // Within a level it's taken in under the same hold of the lock (re-entered, so still one level's), so it's
            // never among the level's players while in none of its regions.
            if (source == destination)
                arrival = this.Arrive(destination, returning: null, out _);
        }

        if (source != destination)
        {
            this.ForgetIn(source);
            arrival = this.Arrive(destination, returning: null, out _);
        }

        if (arrival != Arrival.Refused)
            return true;

        if (destination is not AbstractLevel waitingIn)
            return false;

        waitingIn.KeepWaitingToArrive(this);
        return true;
    }

    /// <summary>
    /// Takes the entity into <paramref name="level"/> if it's still waiting there, after the level couldn't take it in
    /// when it arrived (see <see cref="CommitRespawn"/>). Call holding no other level's transfer lock.
    /// </summary>
    /// <returns>False only when the level refused it again, and it's still waiting.</returns>
    internal bool TryArriveWhereWaiting(AbstractLevel level)
    {
        using (level.TransferLock.Enter())
        {
            // Taken in already, or left the server, which the arrival below sees.
            if (!ReferenceEquals(this.arrivingAt, level))
                return true;

            // The lock is re-entered for the arrival: the same level's, so it's still one level's lock at a time.
            return this.Arrive(level, returning: null, out _) != Arrival.Refused;
        }
    }

    /// <summary>
    /// Moves the entity to <paramref name="position"/> in <paramref name="level"/> when it's still waiting to be taken
    /// in there (a respawned player whose chunk wasn't ready, see <see cref="CommitRespawn"/>, or a traveller no level
    /// took in yet), and takes it in there at once when the level can. Otherwise it waits at its new place, and the
    /// level takes it in on a later tick, as it would have at the old one. Call holding no other level's transfer lock.
    /// </summary>
    /// <remarks>
    /// A waiting entity is in none of the level's regions, so moving it from one region to another as usual would put
    /// it in a region without taking it in: never among the level's players, and refused by every later retry.
    /// </remarks>
    /// <returns>Whether the entity was waiting and has moved; when it wasn't, the caller moves it as usual.</returns>
    private protected bool TryMoveWhileArriving(ILevel level, VectorD position, Angle? yaw, Angle? pitch)
    {
        using (TransferLockOf(level).Enter())
        {
            if (!ReferenceEquals(this.arrivingAt, level))
                return false;

            this.Place(this.Position, position, yaw ?? this.Yaw, pitch ?? this.Pitch);

            // Refused, it's still listed where it waits, which retries it at its new place.
            this.Arrive(level, returning: null, out _);
            return true;
        }
    }

    // The receiving level's half of a handoff, under its transfer lock. When the level can't take the entity in, it
    // hands the entity back toward where it left (when returning says where); otherwise it stays the level's, waiting.
    // A level's refusal of the entity, including adding it failing outright (a plugin closing, say), is reported as
    // such: the entity is then still the caller's to settle, never left half-added.
    private Arrival Arrive(ILevel level, Departure? returning, out Exception? failure)
    {
        failure = null;
        using (TransferLockOf(level).Enter())
        {
            // Settled by someone else first: taken in by a teleport while it waited (see TryMoveWhileArriving), or
            // moved on to another level by a respawn. Nothing is left for this caller to settle.
            if (!ReferenceEquals(this.arrivingAt, level))
                return Arrival.Arrived;

            // Taken or killed on the way: picked up, say, through a lookup that found it before it left, or a player
            // that left the server.
            var removed = this.removedOnArrival
                || this.leftServer
                || this is ItemEntity { Removed: true }
                || this is Mob { IsRemoved: true };
            this.removedOnArrival = false;
            if (removed)
            {
                this.arrivingAt = null;

                // The rest of a removal, whose most was done as the entity left its source.
                if (this is not IPlayer)
                    API.Data.AttachmentStorage.For(this).Retire();

                return Arrival.Removed;
            }

            // Ready before the level publishes it, as its tick may reach it from then on.
            if (this is Mob mob)
            {
                mob.HoldLeashByUuid();
                mob.ResetMovementBaseline();
            }

            // Not into a region or chunk that's unloading, which has already taken the entities it saves.
            var admitted = level is not AbstractLevel destination || destination.AdmitsTravellersAt(this.Position);
            try
            {
                admitted = admitted && level.TryAddEntity(this);
            }
            catch (Exception exception)
            {
                failure = exception;
                admitted = false;
            }

            if (admitted)
            {
                this.arrivingAt = null;
                if (this is IPlayer player)
                    level.TryAddPlayer(player);

                return Arrival.Arrived;
            }

            if (returning is Departure back)
            {
                // Riding links never span levels: whoever boarded it here stays.
                this.LeaveVehicle();
                this.EjectPassengers();
                this.Place(this.Position, back.Position, back.Yaw, back.Pitch);
                this.arrivingAt = back.Level;
                this.Level = back.Level;
            }
            else if (this is IPlayer waiting)
            {
                // A player waiting to be taken in isn't among the level's players either, whether it came from another
                // level or respawned in this one, so the pass after the levels' ticks (see World.DoWorldTickAsync)
                // doesn't simulate it outside the level's entities.
                level.TryRemovePlayer(waiting);
            }

            return Arrival.Refused;
        }
    }

    // Keeps an entity neither level took in as an entity waiting to spawn in the chunk it left, which the chunk saves
    // and the level spawns on its next tick (dropped then, like vanilla's, if its UUID is taken). The instance itself is
    // retired, so that nothing still holding it can take its items.
    private bool KeepWhereItLeft(Departure departure)
    {
        if (this is IPlayer || departure.Level.GetRegionForLocation(departure.Position) is not Region region)
            return false;

        using (departure.Level.TransferLock.Enter())
        {
            if (this.Level != departure.Level)
                return false;

            // A type Obsidian never saves (an experience orb, say) would be gone from any save or unload of its chunk
            // anyway, so it ends here rather than waiting for a place forever.
            if (EntityNbt.Save(this) is null)
            {
                this.arrivingAt = null;
                API.Data.AttachmentStorage.For(this).Retire();
                departure.Level.Logger.LogWarning(
                    "Entity {EntityId} could not be placed after travelling and isn't saved; it was discarded",
                    this.EntityId);
                return true;
            }

            if (!region.TryKeepUnplaced(this, departure.Level))
                return false;

            this.arrivingAt = null;
            if (this is ItemEntity item)
                item.Removed = true;
        }

        departure.Level.Logger.LogWarning(
            "Entity {EntityId} could not be placed after travelling; it was kept in the chunk it left", this.EntityId);
        return true;
    }

    private void Place(VectorD from, VectorD position, Angle yaw, Angle pitch)
    {
        this.LastPosition = from;
        this.Position = position;
        this.Yaw = yaw;
        this.Pitch = pitch;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(position);
    }

    internal static void ValidateTravel(ILevel level, VectorD position, Angle? yaw, Angle? pitch)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)
            || Math.Abs(position.X) >= LevelPortals.WorldBorderLimit
            || Math.Abs(position.Z) >= LevelPortals.WorldBorderLimit
            || position.Y <= int.MinValue || position.Y >= int.MaxValue
            || yaw is Angle y && !float.IsFinite(y.Degrees)
            || pitch is Angle p && !float.IsFinite(p.Degrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), "Travel requires finite coordinates inside the global coordinate limits.");
        }
    }
}
