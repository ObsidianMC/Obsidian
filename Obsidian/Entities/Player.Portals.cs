using Obsidian.API.World;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Portals;

namespace Obsidian.Entities;

public partial class Player
{
    private const int PlayerPortalCooldown = 10;
    private const int NetherPortalRelativeFlags = 0x1E0;
    private int portalTime;
    private int portalCooldown;
    private volatile int portalGeneration;
    private int lastPortal;
    private PortalType? lastPortalType;
    private int lastPortalBinding;
    private Task<PortalDestination?>? portalDestination;
    private bool returningFromEnd;
    private bool preservePortalMotion;
    internal bool SeenCredits { get; set; }
    internal bool AwaitingEndCredits { get; private set; }
    internal int PortalCooldown => this.portalCooldown;

    internal async Task CancelPortalTravelAsync()
    {
        this.portalGeneration++;
        this.portalTime = 0;
        this.lastPortalType = null;
        if (this.portalDestination is not Task<PortalDestination?> pending)
            return;
        this.portalDestination = null;
        (await pending)?.Area.Dispose();
    }

    internal async Task TickPortalsAsync()
    {
        if (!this.Client.Connected || !this.Alive || this.Respawning || this.AwaitingEndCredits)
        {
            this.portalGeneration++;
            if (this.portalDestination is { IsCompleted: true })
                await this.CancelPortalTravelAsync();
            return;
        }
        var contact = await this.FindPortalContactAsync();
        if (this.lastPortalType is not null && contact?.Type != this.lastPortalType)
            this.portalGeneration++;
        if (this.portalDestination is Task<PortalDestination?> pending)
        {
            if (!this.returningFromEnd
                && (contact is null || contact.Value.BlockId != this.lastPortal
                    || contact.Value.Type != this.lastPortalType))
                this.portalGeneration++;
            if (!pending.IsCompleted)
                return;
            this.portalDestination = null;
            var destination = await pending;
            try
            {
                if (destination is not null
                    && (destination.Type is null || destination.Type.Generation == destination.Generation)
                    && (this.returningFromEnd
                        || contact?.BlockId == this.lastPortal && contact?.Type == this.lastPortalType))
                {
                    await this.ChangePortalLevelAsync(destination);
                    this.portalCooldown = PlayerPortalCooldown;
                    this.portalTime = 0;
                }
            }
            finally
            {
                destination?.Area.Dispose();
            }
            this.returningFromEnd = false;
            return;
        }
        if (this.portalCooldown > 0)
        {
            this.portalCooldown = contact is null ? this.portalCooldown - 1 : PlayerPortalCooldown;
            return;
        }
        if (contact is not PortalContact portal)
        {
            this.portalTime = Math.Max(0, this.portalTime - 4);
            return;
        }
        if (portal.BlockId != this.lastPortal || portal.Type != this.lastPortalType
            || this.lastPortalBinding != (portal.Type?.Generation ?? 0))
            this.portalTime = 0;

        this.lastPortal = portal.BlockId;
        this.lastPortalType = portal.Type;
        this.lastPortalBinding = portal.Type?.Generation ?? 0;
        var delay = 0;
        if (portal.Type is PortalType type)
        {
            if (!PortalTypesRegistry.TryEnter(type, out var properties, out var call))
                return;

            using var behavior = call;
            delay = this.Abilities.HasFlag(PlayerAbility.Invulnerable)
                ? properties.CreativeWaitTicks : properties.WaitTicks;
        }

        if (this.portalTime++ < delay)
            return;

        if (portal.BlockId == BlockIds.EndPortal && this.Level.DimensionName == "minecraft:the_end" &&
            this.GameMode != GameMode.Spectator)
        {
            this.AwaitingEndCredits = true;
            await this.Client.QueuePacketAsync(new GameEventPacket(this.SeenCredits
                ? WinStateReason.JustRespawnPlayer : WinStateReason.RollCreditsAndRespawn));
            this.SeenCredits = true;
            return;
        }
        var generation = ++this.portalGeneration;
        this.portalTime = 0;
        this.portalDestination = this.PreparePortalDestinationAsync(portal.Position, portal.BlockId, generation);
    }

    internal async Task<bool> FinishEndCreditsAsync()
    {
        if (!this.AwaitingEndCredits)
            return false;
        this.AwaitingEndCredits = false;
        var generation = ++this.portalGeneration;
        this.lastPortal = BlockIds.EndPortal;
        this.lastPortalType = null;
        this.returningFromEnd = true;
        this.portalDestination =
            this.PreparePortalDestinationAsync((Vector)this.Position, BlockIds.EndPortal, generation);
        return true;
    }

    // Like vanilla's canUsePortal, a passenger doesn't use portals: its root vehicle carries it through.
    private ValueTask<PortalContact?> FindPortalContactAsync() => this.Vehicle is null
        ? PortalTravel.FindContactAsync(this, includeEnd: true)
        : ValueTask.FromResult<PortalContact?>(null);

    private async Task<PortalDestination?> PreparePortalDestinationAsync(Vector entrance, int portal, int generation)
    {
        var source = this.Level;
        var world = source is IDimension dimension ? dimension.ParentWorld as World : source as World;
        if (world is null)
            return null;
        bool CanContinue() => this.Client.Connected && this.Alive && this.Level == source &&
            this.portalGeneration == generation;
        try
        {
            if (portal != BlockIds.EndPortal)
            {
                return this.lastPortalType is PortalType type
                    ? await PortalTravel.PrepareAsync(this, type, entrance, CanContinue)
                    : null;
            }

            var name = source.DimensionName == "minecraft:the_end" ? world.DimensionName : "minecraft:the_end";
            var destination = world.GetLevel(name) as AbstractLevel;
            if (destination is null)
                return null;

            if (portal == BlockIds.EndPortal)
            {
                var enteringEnd = name == "minecraft:the_end";
                var position = enteringEnd ? new VectorD(100.5, 51, 0.5) : world.LevelData.SpawnPosition;
                var area = await PortalArea.LoadAsync(destination, (Vector)position, 3, CanContinue);
                if (area is null)
                    return null;
                try
                {
                    if (enteringEnd)
                        await EndTravel.BuildArrivalPlatformAsync(destination);

                    if (CanContinue())
                        return new(destination, position, enteringEnd ? 90f : this.Yaw,
                            enteringEnd ? 0f : this.Pitch, VectorD.Zero, area);
                    area.Dispose();
                    return null;
                }
                catch
                {
                    area.Dispose();
                    throw;
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            this.Logger.LogError(exception, "Failed to prepare a portal destination for {Username}", this.Username);
            return null;
        }
    }

    private Task ChangePortalLevelAsync(PortalDestination destination) =>
        this.TransferDimensionAsync(
            destination.Level,
            destination.Position,
            destination.Yaw,
            destination.Pitch,
            destination.Motion,
            portal: true,
            relativeMotion: this.lastPortalType is not null);
}
