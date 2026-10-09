using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.World;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Hosting;
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

internal sealed class TestClient(IServer server, bool connected = true) : IClient
{
    private readonly List<IClientboundPacket> packets = [];

    public int Id { get; } = Obsidian.Server.GetNextEntityId();
    public string? Brand { get; set; } = "test-client";
    public string? Ip => "127.0.0.1";
    public bool Connected { get; private set; } = connected;
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
        this.packets.Add(packet);
        return true;
    }

    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(ReadOnlySpan<byte> sharedKey, ReadOnlySpan<byte> verifyToken) => verifyToken;

    public ValueTask DisconnectAsync(ChatMessage reason)
    {
        this.Connected = false;
        return default;
    }

    public ValueTask QueuePacketAsync(IClientboundPacket packet)
    {
        this.packets.Add(packet);
        return default;
    }

    public void SetState(ClientState state) => this.State = state;

    public ValueTask<bool> VerifyProfileAsync() => ValueTask.FromResult(true);

    public void ClearPackets() => this.packets.Clear();

    public void Dispose() => this.Connected = false;
}

internal sealed class RecordingPacketBroadcaster : IPacketBroadcaster
{
    public IServer? Server { get; set; }

    public void Broadcast(IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, excludedIds: excludedIds);

    public void BroadcastTo(IClientboundPacket packet, params int[] ids) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, includedIds: ids);

    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(toLevel.Players.Values.OfType<IClientPlayer>(), packet, excludedIds: excludedIds);

    public void BroadcastToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (level is not AbstractLevel concrete)
            return;

        this.Deliver(concrete.GetPlayersInRange(location, concrete.Configuration.EntityBroadcastRangePercentage).OfType<IClientPlayer>(), packet, excludedIds: excludedIds);
    }

    public void QueuePacketTo(IClientboundPacket packet, params int[] ids) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, includedIds: ids);

    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids) => this.QueuePacketTo(packet, ids);

    public void QueuePacketToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(toLevel.Players.Values.OfType<IClientPlayer>(), packet, excludedIds: excludedIds);

    public void QueuePacketToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (level is not AbstractLevel concrete)
            return;

        this.Deliver(concrete.GetPlayersInRange(location, concrete.Configuration.EntityBroadcastRangePercentage).OfType<IClientPlayer>(), packet, excludedIds: excludedIds);
    }

    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) =>
        this.Deliver(this.Server?.OnlinePlayers.Values.OfType<IClientPlayer>() ?? [], packet, excludedIds: excludedIds);

    public void QueuePacketToLevel(ILevel toLevel, int priority, IClientboundPacket packet, params int[] excludedIds) =>
        this.QueuePacketToLevel(toLevel, packet, excludedIds);

    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) =>
        this.QueuePacket(packet, excludedIds);

    private void Deliver(IEnumerable<IClientPlayer> players, IClientboundPacket packet, int[]? includedIds = null, int[]? excludedIds = null)
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
    public ValueTask<CachedProfile?> GetCachedUserFromNameAsync(string username) => ValueTask.FromResult<CachedProfile?>(new CachedProfile
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

    public Task<MojangProfile?> HasJoinedAsync(string username, string serverId) => Task.FromResult<MojangProfile?>(null);

    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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

    public async ValueTask DisposeAsync()
    {
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
    public static RealServerContext CreateServer(string worldName)
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
        var worldManager = new FakeWorldManager(world);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IServerEnvironment, FakeServerEnvironment>();
        services.AddSingleton(eventDispatcher);
        services.AddSingleton(commandHandler);
        var provider = services.BuildServiceProvider();

        var server = new Server(
            new FakeHostApplicationLifetime(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new TestOptionsMonitor<WhitelistConfiguration>(new WhitelistConfiguration()),
            loggerFactory,
            eventDispatcher,
            provider,
            commandHandler,
            new FakeUserCache(),
            worldManager)
        {
            MaxConnections = 16,
            MaxBufferSize = 8192
        };

        typeof(Server).GetProperty(nameof(Server.Connections))!.SetValue(server, new ConcurrentDictionary<int, IClient>());

        broadcaster.Server = server;
        eventDispatcher.RegisterEvents();

        return new RealServerContext
        {
            Server = server,
            World = world,
            Broadcaster = broadcaster,
            EventDispatcher = eventDispatcher,
            BootstrapProvider = bootstrapProvider,
            Provider = provider,
            WorldName = worldName
        };
    }

    public static Obsidian.Entities.Player CreateConnectedPlayer(Server server, IWorld world, string username, VectorD? position = null)
    {
        var client = new TestClient(server);
        var player = new Obsidian.Entities.Player(Guid.NewGuid(), username, client, world)
        {
            Server = server,
            Position = position ?? world.LevelData.SpawnPosition,
            LastPosition = position ?? world.LevelData.SpawnPosition,
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
        await server.EventDispatcher.ExecuteEventAsync(new Obsidian.API.Events.PlayerJoinEventArgs(player, server, DateTimeOffset.UtcNow));
    }

    private static World CreateWorld(string name, ServerConfiguration configuration, IPacketBroadcaster broadcaster, IEventDispatcher eventDispatcher)
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
                MonsterSpawnLightLevel = new Obsidian.API.Registry.Codecs.Dimensions.MonsterSpawnLightLevel { IntValue = 0 },
                PiglinSafe = false,
                Natural = true,
                AmbientLight = 0,
                RespawnAnchorWorks = false,
                HasSkylight = true,
                BedWorks = true,
                HasRaids = true,
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
