using Microsoft.Extensions.Logging;
using Obsidian.API.World;
using Obsidian.WorldData;
using Obsidian.WorldData.Portals;

namespace Obsidian.Entities;

public partial class Entity
{
    private int travelPortalTime;
    private int travelPortalCooldown;
    private volatile int travelPortalGeneration;
    private PortalType? travelPortalType;
    private int travelPortalBinding;
    private Task<PortalDestination?>? travelPortalDestination;

    /// <summary>Cancels preparation and releases any already prepared chunk tickets when the entity leaves.</summary>
    internal void CancelEntityPortalTravel()
    {
        this.travelPortalGeneration++;
        this.travelPortalTime = 0;
        this.travelPortalType = null;
        var pending = this.travelPortalDestination;
        this.travelPortalDestination = null;
        if (pending is null)
            return;

        if (pending.IsCompletedSuccessfully)
        {
            pending.Result?.Area.Dispose();
            return;
        }

        _ = pending.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                task.Result?.Area.Dispose();
            else
                _ = task.Exception;
        }, TaskScheduler.Default);
    }

    /// <summary>Whether the entity's type uses portals at all.</summary>
    internal virtual bool CanUsePortals => true;

    // Vanilla's getDimensionChangingDelay: a vehicle whose first passenger is a player waits like a player.
    private int TravelPortalCooldown => this is IPlayer || this.Passengers is [IPlayer, ..] ? 10 : 300;

    /// <summary>
    /// The ticks before the entity may use a portal again, which vanilla saves as <c>PortalCooldown</c> (players keep
    /// their own, <see cref="PlayerBase.PortalCooldown"/>).
    /// </summary>
    internal int PortalCooldownTicks
    {
        get => this.travelPortalCooldown;
        set => this.travelPortalCooldown = value;
    }

    /// <summary>
    /// Vanilla's <c>setPortalCooldown()</c>: the entity waits its dimension changing delay before it may use a portal,
    /// as a zombified piglin a portal spawned does.
    /// </summary>
    internal void StartPortalCooldown() => this.travelPortalCooldown = this.TravelPortalCooldown;

    /// <summary>Called once after each non-player entity tick, independently of its movement implementation.</summary>
    internal async Task TickEntityPortalAsync()
    {
        // Like vanilla's canUsePortal, only a living root vehicle uses portals; it carries its passengers along.
        var contact = this.Health > 0 && this.IsActive && this.Vehicle is null && this.CanUsePortals
            ? await PortalTravel.FindContactAsync(this, includeEnd: true, includeGateways: true)
            : null;
        if (this.travelPortalDestination is Task<PortalDestination?> pending)
        {
            // An End portal or gateway trip, which has no portal type, completes once started, as vanilla's
            // happens within the tick of contact.
            var stillInside = this.travelPortalType is null || contact?.Type == this.travelPortalType;
            if (!stillInside)
                this.travelPortalGeneration++;

            if (!pending.IsCompleted)
                return;

            this.travelPortalDestination = null;
            var destination = await pending;
            try
            {
                if (destination is not null && stillInside
                    && (destination.Type is null || destination.Type.Generation == destination.Generation))
                {
                    this.Motion = destination.Motion;

                    // Vanilla Entity's default dimension-change cooldown is 300; players override it to ten. Set while
                    // the entity is still this level's: once through, the destination's tick may reach it at any time.
                    // Teleporting resets the portal time.
                    var cooldown = this.travelPortalCooldown;
                    this.travelPortalCooldown = this.TravelPortalCooldown;
                    try
                    {
                        await this.TeleportAsync(
                            destination.Level, destination.Position, destination.Yaw, destination.Pitch);
                    }
                    catch
                    {
                        this.travelPortalCooldown = cooldown;
                        throw;
                    }

                    this.PlacePortalTicket(destination);
                }
            }
            finally
            {
                destination?.Area.Dispose();
            }

            return;
        }

        if (this.travelPortalCooldown > 0)
        {
            this.travelPortalCooldown = contact is null ? this.travelPortalCooldown - 1 : this.TravelPortalCooldown;
            return;
        }

        if (contact is not PortalContact portal)
        {
            this.travelPortalTime = Math.Max(0, this.travelPortalTime - 4);
            return;
        }

        if (portal.Type is not PortalType type)
        {
            this.StartEndTravel(portal);
            return;
        }

        if (this.travelPortalType != type || this.travelPortalBinding != type.Generation)
            this.travelPortalTime = 0;

        this.travelPortalType = type;
        this.travelPortalBinding = type.Generation;
        if (!PortalTypesRegistry.TryEnter(type, out var properties, out var call))
            return;

        int delay;
        using (call)
        {
            delay = this is IPlayer player
                ? player.Abilities.HasFlag(PlayerAbility.Invulnerable)
                    ? properties.CreativeWaitTicks : properties.WaitTicks
                : properties.EntityWaitTicks;
        }

        if (this.travelPortalTime++ < delay)
            return;

        this.travelPortalTime = 0;
        this.StartTravelPreparation(
            canContinue => PortalTravel.PrepareAsync(this, type, portal.Position, canContinue));
    }

    /// <summary>
    /// Keeps the area around a portal trip's exit loaded for a while, as vanilla's portal transitions do once the
    /// entity is through (see <see cref="PortalDestination"/>), unless the trip places none. The destination's chunks
    /// are still held then, so they stay loaded throughout.
    /// </summary>
    private protected void PlacePortalTicket(PortalDestination destination)
    {
        if (!destination.PlacesTicket || !ReferenceEquals(this.Level, destination.Level))
            return;

        var anchor = destination.TicketAnchor is Vector found ? (VectorD)found : this.Position;
        destination.Level.AddPortalTicket(anchor);
    }

    // End portals and gateways send entities without waiting, like vanilla's zero transition time; a gateway only
    // when it isn't cooling down from the last entity it sent.
    private void StartEndTravel(PortalContact portal)
    {
        this.travelPortalType = null;
        this.travelPortalTime = 0;
        if (portal.BlockId == BlockIds.EndGateway
            && (this.Level is not AbstractLevel level || !level.TryUseEndGateway(portal.Position)))
        {
            return;
        }

        this.StartTravelPreparation(canContinue => EndTravel.PrepareAsync(this, portal, canContinue));
    }

    // Prepares the destination in the background; it lapses if the entity dies, leaves or starts another trip.
    private void StartTravelPreparation(Func<Func<bool>, Task<PortalDestination?>> prepare)
    {
        var generation = ++this.travelPortalGeneration;
        var source = this.Level;
        this.travelPortalDestination = PrepareAsync();

        async Task<PortalDestination?> PrepareAsync()
        {
            try
            {
                return await prepare(() => this.Health > 0
                    && this.Level == source
                    && this.travelPortalGeneration == generation
                    && source is AbstractLevel resident
                    && resident.GetRegionForLocation(this.Position)?.Entities.ContainsKey(this.EntityId) == true);
            }
            catch (Exception exception)
            {
                if (source is AbstractLevel level)
                {
                    level.Logger.LogError(
                        exception, "Failed to prepare a portal destination for entity {Id}", this.EntityId);
                }

                return null;
            }
        }
    }
}
