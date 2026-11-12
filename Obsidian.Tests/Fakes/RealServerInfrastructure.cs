using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Networking;
using Obsidian.API.World;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Hosting;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Services;
using Obsidian.Utilities.Mojang;
using Obsidian.WorldData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Tests.Fakes;

internal sealed class TestClient(IServer server, bool connected = true) : IClient, IConditionalPacketSender
{
    private readonly List<IClientboundPacket> packets = [];

    public int Id { get; } = Obsidian.Server.GetNextEntityId();
    public string? Brand { get; set; } = "test-client";
    public string? Ip => "127.0.0.1";
    public bool Connected { get; private set; } = connected;
    internal ChatMessage? DisconnectReason { get; private set; }
    public long? LastKeepAliveId { get; set; }
    public IServer Server { get; } = server;
    public ClientState State { get; private set; } = ClientState.Play;
    public SignatureData? SignatureData { get; set; }
    public int Ping { get; set; } = 42;
    public ReadOnlyMemory<byte> RandomToken => ReadOnlyMemory<byte>.Empty;
    public IPlayer? Player { get; private set; }
    public ILogger Logger { get; } = NullLogger.Instance;
    public ReadOnlyCollection<IClientboundPacket> SentPackets => this.packets.AsReadOnly();

    public void Attach(IPlayer player) => this.Player = player;

    public bool SendPacket(IClientboundPacket packet)
    {
        this.BeforeSend?.Invoke(packet);
        this.Add(packet);
        return true;
    }

    // Like Client's: level changes counted, and the checks made, under the lock every packet is added under.
    public int LevelEpoch
    {
        get
        {
            lock (this.packets)
                return this.levelEpoch;
        }
    }

    public bool SendPacketIf(SharedPacketBodies shared, int levelEpoch, Func<bool> wanted)
    {
        this.BeforeSend?.Invoke(shared.Packet);
        lock (this.packets)
        {
            if (this.levelEpoch != levelEpoch || !wanted())
                return false;

            this.packets.Add(shared.Packet);
        }

        return true;
    }

    private int levelEpoch;

    private void Add(IClientboundPacket packet)
    {
        lock (this.packets)
        {
            this.packets.Add(packet);
            if (packet is RespawnPacket or LoginPacket)
                this.levelEpoch++;
        }
    }

    // Runs before a packet is sent, on the sending thread: tests can hold a sender there with a gate.
    internal Action<IClientboundPacket>? BeforeSend { get; set; }

    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(
        ReadOnlySpan<byte> sharedKey, ReadOnlySpan<byte> verifyToken) => verifyToken;

    public ValueTask DisconnectAsync(ChatMessage reason)
    {
        this.DisconnectReason = reason;
        this.Dispose();
        return default;
    }

    public ValueTask QueuePacketAsync(IClientboundPacket packet)
    {
        this.Add(packet);
        return this.AfterQueue?.Invoke(packet) ?? default;
    }

    // Tests can stop a travel after its position packet was received, without timers or production callbacks.
    internal Func<IClientboundPacket, ValueTask>? AfterQueue { get; set; }

    public void SetState(ClientState state) => this.State = state;

    public ValueTask<bool> VerifyProfileAsync() => ValueTask.FromResult(true);

    // Payload channels need a real connection's handshake; see ClientChannels.
    internal Obsidian.Net.Payloads.ClientChannels? Channels { get; set; }

    public bool HasChannel(string channel) => this.Channels?.HasChannel(channel) ?? false;

    public ValueTask<bool> SendPayloadAsync<TPayload>(TPayload payload) where TPayload : ICustomPayload<TPayload> =>
        ValueTask.FromResult(false);

    public void ClearPackets()
    {
        lock (this.packets)
            this.packets.Clear();
    }

    // A 26.3 client confirms with its resolved position and rotation, not just the id. Tests can hold an older
    // packet to simulate a delayed confirmation, or omit it to confirm the latest one received.
    public ValueTask ConfirmTeleportAsync(PlayerPositionPacket? teleport = null, VectorD? position = null)
    {
        teleport ??= this.packets.OfType<PlayerPositionPacket>().Last();
        using var buffer = new NetworkBuffer();
        buffer.WriteVarInt(teleport.TeleportId);
        VectorD.Write(position ?? teleport.Position, buffer);
        buffer.WriteSingle(teleport.Yaw);
        buffer.WriteSingle(teleport.Pitch);
        buffer.Offset = 0;

        var confirmation = new AcceptTeleportationPacket();
        confirmation.Populate(buffer);
        return confirmation.HandleAsync(this.Server, (IClientPlayer)this.Player!);
    }

    public void Dispose()
    {
        this.Connected = false;
        if (this.Player is PlayerBase player)
            player.StopTeleportWaits();
    }
}

internal sealed class RecordingPacketBroadcaster : IPacketBroadcaster
{
    private readonly List<Action> queuedRangePackets = [];

    public IServer? Server { get; set; }

    // Opt in when checking ordering against immediate block updates, without a timer or background service.
    public bool DeferRangePackets { get; set; }

    public void FlushRangePackets()
    {
        var pending = this.queuedRangePackets.ToArray();
        this.queuedRangePackets.Clear();
        foreach (var send in pending)
        {
            send();
        }
    }

    public void Broadcast(IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, excludedIds: excludedIds);

    public void BroadcastTo(IClientboundPacket packet, params int[] ids) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, includedIds: ids);

    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(toLevel.Players.Values.OfType<IClientPlayer>(), packet, excludedIds: excludedIds);

    public void BroadcastToLevelInRange(
        ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (level is not AbstractLevel concrete)
            return;

        var players = concrete.GetPlayersInRange(location, concrete.Configuration.EntityBroadcastRangePercentage)
            .OfType<IClientPlayer>();
        this.Deliver(players, packet, excludedIds: excludedIds);
    }

    public void QueuePacketTo(IClientboundPacket packet, params int[] ids) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, includedIds: ids);

    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids) =>
        this.QueuePacketTo(packet, ids);

    public void QueuePacketToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(toLevel.Players.Values.OfType<IClientPlayer>(), packet, excludedIds: excludedIds);

    public void QueuePacketToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet,
        params int[] excludedIds)
    {
        if (level is not AbstractLevel concrete)
            return;

        // Production selects recipients at enqueue time, then sends this batch after immediate broadcasts.
        var players = concrete.GetPlayersInRange(location, concrete.Configuration.EntityBroadcastRangePercentage)
            .OfType<IClientPlayer>().ToArray();
        if (this.DeferRangePackets)
        {
            this.queuedRangePackets.Add(() => this.Deliver(
                players.Where(player => player.Level == level), packet, excludedIds: excludedIds));
            return;
        }

        this.Deliver(players, packet, excludedIds: excludedIds);
    }

    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, excludedIds: excludedIds);

    public void QueuePacketToLevel(ILevel toLevel, int priority, IClientboundPacket packet, params int[] excludedIds) =>
        this.QueuePacketToLevel(toLevel, packet, excludedIds);

    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) =>
        this.QueuePacket(packet, excludedIds);

    private void Deliver(
        IEnumerable<IClientPlayer> players, IClientboundPacket packet,
        int[]? includedIds = null, int[]? excludedIds = null)
    {
        foreach (var player in players)
        {
            if (includedIds != null && !includedIds.Contains(player.EntityId))
                continue;
            if (excludedIds != null && excludedIds.Contains(player.EntityId))
                continue;

            player.Client.SendPacket(packet);
        }
    }
}

internal sealed class FakeUserCache : IUserCache
{
    public ValueTask<CachedProfile?> GetCachedUserFromNameAsync(string username) =>
        ValueTask.FromResult<CachedProfile?>(new CachedProfile
        {
            Name = username,
            Uuid = Guid.NewGuid(),
            ExpiresOn = DateTimeOffset.MaxValue
        });

    public ValueTask<CachedProfile> GetCachedUserFromUuidAsync(Guid uuid) => ValueTask.FromResult(new CachedProfile
    {
        Name = uuid.ToString("N"),
        Uuid = uuid,
        ExpiresOn = DateTimeOffset.MaxValue
    });

    public Task<MojangProfile?> HasJoinedAsync(string username, string serverId) =>
        Task.FromResult<MojangProfile?>(null);

    /// <summary>
    /// Whether the cache was saved.
    /// </summary>
    public bool Saved { get; private set; }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        this.Saved = true;
        return Task.CompletedTask;
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}

internal sealed class FakeServerEnvironment : IServerEnvironment
{
    public ValueTask OnServerStoppedGracefullyAsync() => default;
    public ValueTask OnServerCrashAsync(Exception e) => default;
}

internal sealed class RealServerContext : IAsyncDisposable
{
    public required Server Server { get; init; }
    public required World World { get; init; }
    public required RecordingPacketBroadcaster Broadcaster { get; init; }
    public required EventDispatcher EventDispatcher { get; init; }
    public required IServiceProvider BootstrapProvider { get; init; }
    public required IServiceProvider Provider { get; init; }
    public required string WorldName { get; init; }
    public required FakeUserCache UserCache { get; init; }

    public async ValueTask DisposeAsync()
    {
        // The server's loop never ran, so nothing stops it, and a stop is what waits for the work the ticks left to the
        // background, such as the save of a player who changed level from a tick. That work writes into the world's
        // folder, so it ends before the world is disposed and its folder deleted, as the stop has it end first.
        await this.Server.DrainBackgroundWorkAsync();

        this.Server.Dispose();
        this.EventDispatcher.Dispose();
        await this.World.DisposeAsync();
        (this.Provider as IDisposable)?.Dispose();
        (this.BootstrapProvider as IDisposable)?.Dispose();
        var path = Path.Combine("worlds", this.WorldName);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}

internal static class TestServerFactory
{
    /// <summary>
    /// The timeout, in milliseconds, of a test running a server: a guard against hangs, with room for the slowdown
    /// of the whole suite running at once, under which a test taking a fraction of a second alone can take several.
    /// </summary>
    public const int TestTimeout = 30_000;

    /// <param name="worldManager">
    /// Makes the server's world manager for its world; a <see cref="FakeWorldManager"/> by default.
    /// </param>
    public static RealServerContext CreateServer(string worldName, Func<World, IWorldManager>? worldManager = null)
    {
        var configuration = new ServerConfiguration
        {
            OnlineMode = false,
            SpawnChunkRadius = 0,
            ViewDistance = 3,
            SimulationDistance = 5,
            TimeTickSpeedMultiplier = 1
        };

        var bootstrapServices = new ServiceCollection();
        bootstrapServices.AddLogging();
        bootstrapServices.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        bootstrapServices.AddSingleton<IServerEnvironment, FakeServerEnvironment>();
        var bootstrapProvider = bootstrapServices.BuildServiceProvider();

        var loggerFactory = bootstrapProvider.GetRequiredService<ILoggerFactory>();
        var eventDispatcher = new EventDispatcher(loggerFactory.CreateLogger<EventDispatcher>(), bootstrapProvider);
        var commandHandler = new CommandHandler(bootstrapProvider, loggerFactory.CreateLogger<CommandHandler>());

        var broadcaster = new RecordingPacketBroadcaster();
        var world = CreateWorld(worldName, configuration, broadcaster, eventDispatcher);
        var manager = worldManager?.Invoke(world) ?? new FakeWorldManager(world);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IServerEnvironment, FakeServerEnvironment>();
        services.AddSingleton(eventDispatcher);
        services.AddSingleton(commandHandler);

        // Boss bars the server creates send through it.
        services.AddSingleton<IPacketBroadcaster>(broadcaster);
        var provider = services.BuildServiceProvider();

        var userCache = new FakeUserCache();
        var server = new Server(
            new FakeHostApplicationLifetime(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new TestOptionsMonitor<WhitelistConfiguration>(new WhitelistConfiguration()),
            loggerFactory,
            eventDispatcher,
            provider,
            commandHandler,
            userCache,
            manager)
        {
            MaxConnections = 16,
            MaxBufferSize = 8192
        };

        typeof(Server).GetProperty(nameof(Server.Connections))!
            .SetValue(server, new ConcurrentDictionary<int, IClient>());
        typeof(Server).GetProperty(nameof(Server.ScoreboardManager))!
            .SetValue(server, new ScoreboardManager(server, loggerFactory));

        broadcaster.Server = server;
        eventDispatcher.RegisterEvents();

        // As the level factory does: the level runs its command blocks' commands on this server.
        world.OwningServer = server;

        return new RealServerContext
        {
            Server = server,
            World = world,
            Broadcaster = broadcaster,
            EventDispatcher = eventDispatcher,
            BootstrapProvider = bootstrapProvider,
            Provider = provider,
            WorldName = worldName,
            UserCache = userCache
        };
    }

    public static Obsidian.Entities.Player CreateConnectedPlayer(
        Server server, IWorld world, string username, VectorD? position = null, Guid? uuid = null)
    {
        var client = new TestClient(server);
        var spawn = position ?? world.LevelData.SpawnPosition;
        var player = new Obsidian.Entities.Player(uuid ?? Guid.NewGuid(), username, client, world)
        {
            Server = server,
            Position = spawn,
            LastPosition = spawn
        };
        client.Attach(player);
        server.Connections.TryAdd(client.Id, client);
        return player;
    }

    public static async Task JoinConnectedPlayerAsync(Server server, Obsidian.Entities.Player player)
    {
        if (!server.AddPlayer(player))
            throw new InvalidOperationException($"Player '{player.Username}' is already online.");

        await player.UpdatePlayerInfoAsync();
        await player.SendPlayerInfoAsync();
        var joined = new Obsidian.API.Events.PlayerJoinEventArgs(player, server, DateTimeOffset.UtcNow);
        await server.EventDispatcher.ExecuteEventAsync(joined);

        if (player.HasPendingTeleport)
            await ((TestClient)player.Client).ConfirmTeleportAsync();

        // A real client reports its position right after joining, which is when it starts seeing the players around it.
        await player.UpdateAsync(player.Position, MovementFlags.OnGround);
    }

    private static World CreateWorld(
        string name, ServerConfiguration configuration,
        IPacketBroadcaster broadcaster, IEventDispatcher eventDispatcher)
    {
        var world = new World(
            NullLogger<World>.Instance,
            new FakeWorldManager(defaultWorld: null!),
            broadcaster,
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            eventDispatcher,
            new Obsidian.WorldData.Generators.EmptyWorldGenerator(),
            name,
            "test-seed");

        world.Initialize(new Obsidian.API.Registry.Codecs.Dimensions.DimensionCodec
        {
            Name = "minecraft:overworld",
            Id = 0,
            Element = new Obsidian.API.Registry.Codecs.Dimensions.DimensionElement
            {
                MonsterSpawnBlockLightLimit = 0,
                MonsterSpawnLightLevel = new Obsidian.API.Registry.Codecs.Dimensions.MonsterSpawnLightLevel
                {
                    IntValue = 0
                },
                PiglinSafe = false,
                AmbientLight = 0,
                HasSkylight = true,
                MinY = -64,
                Height = 384,
                LogicalHeight = 384,
                CoordinateScale = 1,
                Ultrawarm = false,
                HasCeiling = false
            }
        });

        world.LevelData.SpawnPosition = new VectorD(0.5, 64, 0.5);
        world.LoadRegionByChunk(0, 0);
        return world;
    }
}
