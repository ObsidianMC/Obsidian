using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.WorldData;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Plugins.ServiceProviders;

/// <summary>
/// The host services a plugin is given, as instances of its own: <see cref="IServer"/> and
/// <see cref="ILevelFactory"/>, whose scoreboards, boss bars, recipes and generators are the plugin's, so that
/// unloading it removes them. The rest is the server's services.
/// </summary>
/// <remarks>
/// The plugin is known from the instance it calls rather than from the code that calls it, so what it adds is its own
/// even when a library it uses, an async continuation or inlined code makes the call. The server's own code calls the
/// server's instances, so what it adds stays.
/// </remarks>
internal sealed class PluginHostServices : IServiceProvider
{
    private readonly IServiceProvider serverServices;

    /// <param name="plugin">The plugin.</param>
    /// <param name="server">The server; the plugin's instance credits the plugin only when it's Obsidian's.</param>
    /// <param name="serverServices">The server's services.</param>
    public PluginHostServices(PluginContainer plugin, IServer server, IServiceProvider serverServices)
    {
        this.serverServices = serverServices;
        this.Server = server is Server concrete ? new PluginServer(concrete, plugin) : server;

        if (serverServices.GetService<ILevelFactory>() is LevelFactory factory)
            this.LevelFactory = new PluginLevelFactory(factory, plugin);
    }

    /// <summary>
    /// The plugin's <see cref="IServer"/>, which it's given in its callbacks too.
    /// </summary>
    public IServer Server { get; }

    /// <summary>
    /// The plugin's <see cref="ILevelFactory"/>, or null when the server has none.
    /// </summary>
    public ILevelFactory? LevelFactory { get; }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServer))
            return this.Server;

        if (serviceType == typeof(ILevelFactory) && this.LevelFactory is not null)
            return this.LevelFactory;

        return this.serverServices.GetService(serviceType);
    }

    /// <summary>
    /// Adds the plugin's instances to its services, so that its own services get them too.
    /// </summary>
    public void AddTo(IServiceCollection services)
    {
        services.AddSingleton(this.Server);

        if (this.LevelFactory is not null)
            services.AddSingleton(this.LevelFactory);
    }
}

/// <summary>
/// A plugin's <see cref="IServer"/>: the server, whose scoreboards, boss bars and recipes created through it are the
/// plugin's.
/// </summary>
internal sealed class PluginServer(Server server, PluginContainer plugin) : IServer
{
    private PluginScoreboardManager? scoreboards;

    public string Version => server.Version;

    public int Port => server.Port;

    public int Tps => server.Tps;

    public DateTimeOffset StartTime => server.StartTime;

    public ProtocolVersion Protocol => server.Protocol;

    public IOperatorList Operators => server.Operators;

    public IWorld DefaultWorld => server.DefaultWorld;

    public ServerConfiguration Configuration => server.Configuration;

    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers => server.OnlinePlayers;

    public ConcurrentDictionary<int, IClient> Connections => server.Connections;

    [Obsolete("Channels are per connection; use IClient.HasChannel.")]
    public HashSet<string> RegisteredChannels => server.RegisteredChannels;

    public ReadOnlyMemory<byte> BrandData => server.BrandData;

    public ICommandHandler CommandHandler => server.CommandHandler;

    /// <summary>
    /// The server's scoreboards, of which those this creates are the plugin's. Null until the server is ready.
    /// </summary>
    public IScoreboardManager ScoreboardManager =>
        server.ScoreboardManager is ScoreboardManager manager
            ? this.scoreboards ??= new PluginScoreboardManager(manager, plugin)
            : server.ScoreboardManager;

    public IEventDispatcher EventDispatcher => server.EventDispatcher;

    public IWorldManager WorldManager => server.WorldManager;

    public Task RunAsync() => server.RunAsync();

    public bool IsPlayerOnline(string username) => server.IsPlayerOnline(username);

    public bool IsPlayerOnline(Guid uuid) => server.IsPlayerOnline(uuid);

    public bool IsWhitelisted(string username) => server.IsWhitelisted(username);

    public bool IsWhitelisted(Guid uuid) => server.IsWhitelisted(uuid);

    public void BroadcastMessage(ChatMessage message) => server.BroadcastMessage(message);

    public void BroadcastMessage(IWorld world, ChatMessage message) => server.BroadcastMessage(world, message);

    public IPlayer? GetPlayer(string username) => server.GetPlayer(username);

    public IPlayer? GetPlayer(Guid uuid) => server.GetPlayer(uuid);

    public IPlayer? GetPlayer(int entityId) => server.GetPlayer(entityId);

    public bool TryGetPlayer(string username, [NotNullWhen(true)] out IPlayer? player) =>
        server.TryGetPlayer(username, out player);

    public bool TryGetPlayer(Guid uuid, [NotNullWhen(true)] out IPlayer? player) =>
        server.TryGetPlayer(uuid, out player);

    public bool TryGetPlayer(int entityId, [NotNullWhen(true)] out IPlayer? player) =>
        server.TryGetPlayer(entityId, out player);

    public void RegisterRecipes(params IRecipe[] recipes) => server.RegisterRecipes(recipes, plugin);

    public bool AddPlayer(IPlayer player) => server.AddPlayer(player);

    public bool RemovePlayer(IPlayer player) => server.RemovePlayer(player);

    public Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null) =>
        server.AddServerPlayerAsync(uuid, username, world);

    public Task<bool> RemoveServerPlayerAsync(IPlayer player) => server.RemoveServerPlayerAsync(player);

    public IBossBar CreateBossBar(
        ChatMessage title,
        float health,
        BossBarColor color,
        BossBarDivisionType divisionType,
        BossBarFlags flags) =>
        server.CreateBossBar(title, health, color, divisionType, flags, plugin);

    public void Dispose() => server.Dispose();
}

/// <summary>
/// A plugin's <see cref="IScoreboardManager"/>: the server's, whose scoreboards created through it are the plugin's.
/// </summary>
internal sealed class PluginScoreboardManager(ScoreboardManager manager, PluginContainer plugin) : IScoreboardManager
{
    public IScoreboard DefaultScoreboard => manager.DefaultScoreboard;

    public IScoreboard CreateScoreboard(string name) => manager.CreateScoreboard(name, plugin);
}

/// <summary>
/// A plugin's <see cref="ILevelFactory"/>: the server's, whose generators registered through it are the plugin's.
/// </summary>
internal sealed class PluginLevelFactory(LevelFactory factory, PluginContainer plugin) : ILevelFactory
{
    public IWorld CreateWorld(string name, string seed, string generatorId) =>
        factory.CreateWorld(name, seed, generatorId);

    public IDimension CreateDimension(IWorld parentWorld, string name, string generatorId) =>
        factory.CreateDimension(parentWorld, name, generatorId);

    public void Initialize() => factory.Initialize();

    public void RegisterGenerator<T>() where T : ILevelGenerator, new() => factory.RegisterGenerator<T>(plugin);
}
