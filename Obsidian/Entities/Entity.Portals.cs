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

    private int TravelPortalCooldown => this is IPlayer || this is Mob { TravelPassengers.Count: > 0 } ? 10 : 300;

    /// <summary>Called once after each non-player entity tick, independently of its movement implementation.</summary>
    internal async Task TickEntityPortalAsync()
    {
        var contact = this.Health > 0 ? await PortalTravel.FindContactAsync(this) : null;
        if (this.travelPortalDestination is Task<PortalDestination?> pending)
        {
            if (contact?.Type != this.travelPortalType)
                this.travelPortalGeneration++;

            if (!pending.IsCompleted)
                return;

            this.travelPortalDestination = null;
            var destination = await pending;
            try
            {
                if (destination is not null && contact?.Type == this.travelPortalType
                    && destination.Type?.Generation == destination.Generation)
                {
                    this.Motion = destination.Motion;
                    await this.TeleportAsync(
                        destination.Level, destination.Position, destination.Yaw, destination.Pitch);
                    // Vanilla Entity's default dimension-change cooldown is 300; players override it to ten.
                    this.travelPortalCooldown = this.TravelPortalCooldown;
                    this.travelPortalTime = 0;
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

        if (contact is not PortalContact portal || portal.Type is not PortalType type)
        {
            this.travelPortalTime = Math.Max(0, this.travelPortalTime - 4);
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
            delay = this is IPlayer player
                ? player.Abilities.HasFlag(PlayerAbility.Invulnerable)
                    ? properties.CreativeWaitTicks : properties.WaitTicks
                : properties.EntityWaitTicks;

        if (this.travelPortalTime++ < delay)
            return;

        var generation = ++this.travelPortalGeneration;
        var source = this.Level;
        this.travelPortalTime = 0;
        this.travelPortalDestination = PrepareAsync();

        async Task<PortalDestination?> PrepareAsync()
        {
            try
            {
                return await PortalTravel.PrepareAsync(this, type, portal.Position,
                    () => this.Health > 0 && this.Level == source && this.travelPortalGeneration == generation
                        && source is AbstractLevel resident
                        && resident.GetRegionForLocation(this.Position)?.Entities.ContainsKey(this.EntityId) == true);
            }
            catch (Exception exception)
            {
                if (source is AbstractLevel level)
                    level.Logger.LogError(
                        exception, "Failed to prepare a portal destination for entity {Id}", this.EntityId);

                return null;
            }
        }
    }
}
