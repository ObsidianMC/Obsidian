using Obsidian.API;
using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Obsidian.Tests.Fakes;
public sealed class FakeServer : IServer
{
    public string Version => throw new NotImplementedException();

    public int Port => throw new NotImplementedException();

    public int Tps => throw new NotImplementedException();

    public DateTimeOffset StartTime => throw new NotImplementedException();

    public ProtocolVersion Protocol => throw new NotImplementedException();

    public IOperatorList Operators => throw new NotImplementedException();

    public IWorld DefaultWorld => throw new NotImplementedException();

    public ServerConfiguration Configuration => throw new NotImplementedException();

    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers => throw new NotImplementedException();

    public ConcurrentDictionary<int, IClient> Connections => throw new NotImplementedException();

    public HashSet<string> RegisteredChannels => throw new NotImplementedException();

    public byte[] BrandData => throw new NotImplementedException();

    public ICommandHandler CommandHandler => throw new NotImplementedException();

    public IScoreboardManager ScoreboardManager => throw new NotImplementedException();

    public IEventDispatcher EventDispatcher => throw new NotImplementedException();
    public IWorldManager WorldManager => throw new NotImplementedException();

    public bool AddPlayer(IPlayer player) => throw new NotImplementedException();
    public void BroadcastMessage(ChatMessage message) => throw new NotImplementedException();
    public void BroadcastMessage(IWorld world, ChatMessage message) => throw new NotImplementedException();
    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags) => throw new NotImplementedException();
    public void Dispose() => throw new NotImplementedException();
    public IPlayer GetPlayer(string username) => throw new NotImplementedException();
    public IPlayer GetPlayer(Guid uuid) => throw new NotImplementedException();
    public IPlayer GetPlayer(int entityId) => throw new NotImplementedException();
    public bool IsPlayerOnline(string username) => throw new NotImplementedException();
    public bool IsPlayerOnline(Guid uuid) => throw new NotImplementedException();
    public bool IsWhitelisted(string username) => throw new NotImplementedException();
    public bool IsWhitelisted(Guid uuid) => throw new NotImplementedException();
    public void RegisterRecipes(params IRecipe[] recipes) => throw new NotImplementedException();
    public bool RemovePlayer(IPlayer player) => throw new NotImplementedException();
    public Task RunAsync() => throw new NotImplementedException();
    public bool TryGetPlayer(string username, [NotNullWhen(true)] out IPlayer player) => throw new NotImplementedException();
    public bool TryGetPlayer(Guid uuid, [NotNullWhen(true)] out IPlayer player) => throw new NotImplementedException();
    public bool TryGetPlayer(int entityId, [NotNullWhen(true)] out IPlayer player) => throw new NotImplementedException();
}
