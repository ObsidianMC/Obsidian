using Obsidian.API;
using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.World;
using Obsidian.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Tests.Fakes;

public sealed class FakeServer : IServer
{
    private readonly ConcurrentDictionary<string, Guid> usernames = new(StringComparer.OrdinalIgnoreCase);

    public FakeServer() : this(TestWorldFactory.Create($"fake-server-{Guid.NewGuid():N}")) { }

    public FakeServer(IWorld defaultWorld, IEventDispatcher? eventDispatcher = null, IWorldManager? worldManager = null)
    {
        this.DefaultWorld = defaultWorld;
        this.EventDispatcher = eventDispatcher ?? new FakeEventDispatcher();
        this.WorldManager = worldManager ?? new FakeWorldManager(defaultWorld);
    }

    public string Version => "test";

    public int Port => 25565;

    public int Tps => 20;

    public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;

    public ProtocolVersion Protocol => default;

    public IOperatorList Operators => throw new NotImplementedException();

    public IWorld DefaultWorld { get; }

    public ServerConfiguration Configuration { get; } = new();

    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers { get; } = [];

    public ConcurrentDictionary<int, IClient> Connections { get; } = [];

    public HashSet<string> RegisteredChannels { get; } = [];

    public ReadOnlyMemory<byte> BrandData => ReadOnlyMemory<byte>.Empty;

    public ICommandHandler CommandHandler => throw new NotImplementedException();

    public IScoreboardManager ScoreboardManager => throw new NotImplementedException();

    public IEventDispatcher EventDispatcher { get; }

    public IWorldManager WorldManager { get; }

    public bool AddPlayer(IPlayer player)
    {
        this.usernames[player.Username] = player.Uuid;
        return this.OnlinePlayers.TryAdd(player.Uuid, player);
    }

    public async Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null)
    {
        var player = new ServerPlayer(uuid, username, this, world ?? this.DefaultWorld);
        if (!this.AddPlayer(player))
            throw new InvalidOperationException($"Player '{username}' ({uuid}) is already online.");

        player.Level.TryAddPlayer(player);
        await this.EventDispatcher.ExecuteEventAsync(new API.Events.PlayerJoinEventArgs(player, this, DateTimeOffset.UtcNow));
        return player;
    }

    public void BroadcastMessage(ChatMessage message) { }

    public void BroadcastMessage(IWorld world, ChatMessage message) { }

    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags) =>
        throw new NotImplementedException();

    public void Dispose() => this.EventDispatcher.Dispose();

    public IPlayer? GetPlayer(string username) =>
        this.usernames.TryGetValue(username, out var uuid) && this.OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

    public IPlayer? GetPlayer(Guid uuid) => this.OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

    public IPlayer? GetPlayer(int entityId) => this.OnlinePlayers.Values.FirstOrDefault(player => player.EntityId == entityId);

    public bool IsPlayerOnline(string username) => this.GetPlayer(username) is not null;

    public bool IsPlayerOnline(Guid uuid) => this.OnlinePlayers.ContainsKey(uuid);

    public bool IsWhitelisted(string username) => true;

    public bool IsWhitelisted(Guid uuid) => true;

    public void RegisterRecipes(params IRecipe[] recipes) { }

    public bool RemovePlayer(IPlayer player)
    {
        this.usernames.TryRemove(player.Username, out _);
        player.Level.TryRemovePlayer(player);
        return this.OnlinePlayers.TryRemove(player.Uuid, out _);
    }

    public async Task<bool> RemoveServerPlayerAsync(IPlayer player)
    {
        await this.EventDispatcher.ExecuteEventAsync(new API.Events.PlayerLeaveEventArgs(player, this, DateTimeOffset.UtcNow));
        return this.RemovePlayer(player);
    }

    public Task RunAsync() => Task.CompletedTask;

    public bool TryGetPlayer(string username, [NotNullWhen(true)] out IPlayer? player)
    {
        player = this.GetPlayer(username);
        return player is not null;
    }

    public bool TryGetPlayer(Guid uuid, [NotNullWhen(true)] out IPlayer? player) => this.OnlinePlayers.TryGetValue(uuid, out player);

    public bool TryGetPlayer(int entityId, [NotNullWhen(true)] out IPlayer? player)
    {
        player = this.GetPlayer(entityId);
        return player is not null;
    }
}
