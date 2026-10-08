using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Portals;

namespace Obsidian.Entities;

public partial class Player
{
    private const int SurvivalPortalDelay = 80;
    private const int CreativePortalDelay = 1;
    private const int PlayerPortalCooldown = 10;
    private const int NetherPortalRelativeFlags = 0x1E0;
    private int portalTime;
    private int portalCooldown;
    private volatile int portalGeneration;
    private Material lastPortal;
    private Task<PortalDestination?>? portalDestination;
    private bool returningFromEnd;
    private bool preservePortalMotion;
    internal bool SeenCredits { get; set; }
    internal bool AwaitingEndCredits { get; private set; }
    internal int PortalCooldown => this.portalCooldown;

    private sealed record PortalDestination(AbstractLevel Level, VectorD Position, Angle Yaw, Angle Pitch, VectorD Motion,
        PortalArea Area);

    internal async Task CancelPortalTravelAsync()
    {
        this.portalGeneration++;
        if (this.portalDestination is not { } pending)
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
        if (this.portalDestination is { } pending)
        {
            if (!this.returningFromEnd && (contact is null || contact.Value.Material != this.lastPortal))
                this.portalGeneration++;
            if (!pending.IsCompleted)
                return;
            this.portalDestination = null;
            var destination = await pending;
            try
            {
                if (destination is not null && (this.returningFromEnd || contact?.Material == this.lastPortal))
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
        if (contact is not { } portal)
        {
            this.portalTime = Math.Max(0, this.portalTime - 4);
            return;
        }
        if (portal.Material != this.lastPortal)
            this.portalTime = 0;
        this.lastPortal = portal.Material;
        var delay = portal.Material == Material.EndPortal ? 0 :
            this.Abilities.HasFlag(PlayerAbility.Invulnerable) ? CreativePortalDelay : SurvivalPortalDelay;
        if (this.portalTime++ < delay)
            return;

        if (portal.Material == Material.EndPortal && this.Level.DimensionName == "minecraft:the_end" &&
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
        this.portalDestination = this.PreparePortalDestinationAsync(portal.Position, portal.Material, generation);
    }

    internal async Task<bool> FinishEndCreditsAsync()
    {
        if (!this.AwaitingEndCredits)
            return false;
        this.AwaitingEndCredits = false;
        var generation = ++this.portalGeneration;
        this.lastPortal = Material.EndPortal;
        this.returningFromEnd = true;
        this.portalDestination = this.PreparePortalDestinationAsync((Vector)this.Position, Material.EndPortal, generation);
        return true;
    }

    private async ValueTask<(Vector Position, Material Material)?> FindPortalContactAsync()
    {
        var width = this.Dimension.Width > 0 ? this.Dimension.Width : 0.6;
        var height = this.Dimension.Height > 0 ? this.Dimension.Height : this.Swimming ? 0.6 : this.Sneaking ? 1.5 : 1.8;
        var min = new VectorD(this.Position.X - width / 2, this.Position.Y, this.Position.Z - width / 2);
        var max = new VectorD(this.Position.X + width / 2, this.Position.Y + height, this.Position.Z + width / 2);
        for (var x = (int)Math.Floor(min.X + 1e-7); x <= (int)Math.Floor(max.X - 1e-7); x++)
            for (var y = (int)Math.Floor(min.Y + 1e-7); y <= (int)Math.Floor(max.Y - 1e-7); y++)
                for (var z = (int)Math.Floor(min.Z + 1e-7); z <= (int)Math.Floor(max.Z - 1e-7); z++)
                {
                    var position = new Vector(x, y, z);
                    var block = await this.Level.GetBlockAsync(position);
                    if (block?.Material == Material.EndPortal && min.Y < y + 0.75 && max.Y > y + 0.375)
                        return (position, Material.EndPortal);
                    if (block?.Material != Material.NetherPortal)
                        continue;
                    if (block.GetProperty("axis") == "x" ? min.Z < z + 0.625 && max.Z > z + 0.375 :
                        min.X < x + 0.625 && max.X > x + 0.375)
                        return (position, Material.NetherPortal);
                }
        return null;
    }

    private async Task<PortalDestination?> PreparePortalDestinationAsync(Vector entrance, Material portal, int generation)
    {
        var source = this.Level;
        var world = source is IDimension dimension ? dimension.ParentWorld as World : source as World;
        if (world is null)
            return null;
        bool CanContinue() => this.Client.Connected && this.Alive && this.Level == source &&
            this.portalGeneration == generation;
        try
        {
            var name = portal == Material.EndPortal
                ? source.DimensionName == "minecraft:the_end" ? world.DimensionName : "minecraft:the_end"
                : source.DimensionName == "minecraft:the_nether" ? world.DimensionName : "minecraft:the_nether";
            var destination = name == world.DimensionName ? world : world.dimensions.GetValueOrDefault(name) as AbstractLevel;
            if (destination is null)
                return null;

            if (portal == Material.EndPortal)
            {
                var enteringEnd = name == "minecraft:the_end";
                var position = enteringEnd ? new VectorD(100.5, 51, 0.5) : world.LevelData.SpawnPosition;
                var area = await PortalArea.LoadAsync(destination, (Vector)position, 3, CanContinue);
                if (area is null)
                    return null;
                try
                {
                    if (enteringEnd)
                    {
                        // EndPlatformFeature, anchored one block below END_SPAWN_POINT (100,50,0).
                        for (var x = 98; x <= 102; x++)
                            for (var z = -2; z <= 2; z++)
                                for (var y = 48; y <= 51; y++)
                                    await destination.SetBlockAsync(x, y, z, y == 48 ? BlocksRegistry.Get(Material.Obsidian) : BlocksRegistry.Air, true);
                    }
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

            var entranceBlock = await source.GetBlockAsync(entrance);
            var axis = entranceBlock?.GetProperty("axis") ?? "x";
            var sourceFrame = await LevelPortals.RectangleAsync(source.GetBlockAsync, entrance, axis);
            CodecRegistry.TryGetDimension(source.DimensionName, out var sourceCodec);
            CodecRegistry.TryGetDimension(destination.DimensionName, out var destinationCodec);
            var scale = (sourceCodec?.Element.CoordinateScale ?? 1) / (destinationCodec?.Element.CoordinateScale ?? 1);
            var target = new Vector((int)Math.Floor(Math.Clamp(this.Position.X * scale,
                -LevelPortals.WorldBorderLimit + 1, LevelPortals.WorldBorderLimit - 2)),
                (int)Math.Floor(this.Position.Y), (int)Math.Floor(Math.Clamp(this.Position.Z * scale,
                -LevelPortals.WorldBorderLimit + 1, LevelPortals.WorldBorderLimit - 2)));
            var width = this.Dimension.Width > 0 ? this.Dimension.Width : 0.6;
            var height = this.Dimension.Height > 0 ? this.Dimension.Height : 1.8;
            var along = axis == "x" ? this.Position.X - sourceFrame.Origin.X : this.Position.Z - sourceFrame.Origin.Z;
            var horizontal = sourceFrame.Width > width ? Math.Clamp((along - width / 2) / (sourceFrame.Width - width), 0, 1) : 0.5;
            var vertical = sourceFrame.Height > height ? Math.Clamp((this.Position.Y - sourceFrame.Origin.Y) / (sourceFrame.Height - height), 0, 1) : 0;
            var normal = axis == "x" ? this.Position.Z - sourceFrame.Origin.Z - 0.5 : this.Position.X - sourceFrame.Origin.X - 0.5;
            var frame = await destination.Portals.FindOrCreateExitAsync(target, axis, CanContinue);
            if (frame is null || !CanContinue())
                return null;
            var exit = frame.Value;
            this.Logger.LogInformation("Nether portal travel for {Username}: {SourceDimension} {Entrance} -> {DestinationDimension}, search target {Target}, exit {Exit}",
                this.Username, source.DimensionName, entrance, destination.DimensionName, target, exit.Origin);
            var h = width / 2 + (exit.Width - width) * horizontal;
            var v = (exit.Height - height) * vertical;
            var positionAtExit = new VectorD(exit.Origin.X + (exit.Axis == "x" ? h : 0.5 + normal),
                exit.Origin.Y + v, exit.Origin.Z + (exit.Axis == "z" ? h : 0.5 + normal));
            var rotation = axis == exit.Axis ? 0f : 90f;
            var motion = rotation == 0 ? this.Motion : new VectorD(-this.Motion.Z, this.Motion.Y, this.Motion.X);
            var exitArea = await PortalArea.LoadAsync(destination, (Vector)positionAtExit, 5, CanContinue);
            if (exitArea is null)
                return null;
            try
            {
                positionAtExit = PortalCollision.FindFreePosition(exitArea.Read, positionAtExit, width, height);
                if (CanContinue())
                    return new(destination, positionAtExit, (float)this.Yaw + rotation, this.Pitch, motion, exitArea);
                exitArea.Dispose();
                return null;
            }
            catch
            {
                exitArea.Dispose();
                throw;
            }
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

    private async Task ChangePortalLevelAsync(PortalDestination destination)
    {
        var previous = this.Level;
        var from = this.Position;
        await previous.DestroyEntityAsync(this);
        previous.TryRemovePlayer(this);
        foreach (var other in previous.Players.Values.OfType<Player>())
            other.visiblePlayers.Remove(this);
        this.Level = destination.Level;
        this.LastPosition = from;
        this.Position = destination.Position;
        this.Yaw = destination.Yaw;
        this.Pitch = destination.Pitch;
        this.Motion = destination.Motion;
        this.FallDistance = 0;
        this.Level.TryAddPlayer(this);
        this.Level.TryAddEntity(this);
        this.portalCooldown = PlayerPortalCooldown;
        this.preservePortalMotion = this.lastPortal == Material.NetherPortal;
        try
        {
            await this.RespawnAsync(DataKept.Attributes | DataKept.Metadata);
        }
        finally
        {
            this.preservePortalMotion = false;
        }
        var world = this.Level is IDimension dimension ? dimension.ParentWorld : this.Level as IWorld;
        await this.Client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            Dimension = world!.DimensionName,
            Pos = (Vector)world.LevelData.SpawnPosition
        }, 0, 0));
        await this.Client.QueuePacketAsync(new SetTimePacket(this.Level.LevelData.Time, this.Level.LevelData.DayTime, true));
        await this.Client.QueuePacketAsync(new GameEventPacket(this.Level.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        await this.Client.QueuePacketAsync(new PlayerAbilitiesPacket { Abilities = this.Abilities });
        await this.SendPlayerInfoAsync();
        await this.EventDispatcher.ExecuteEventAsync(new PlayerTeleportEventArgs(this, this.Server, from, this.Position));
    }
}
