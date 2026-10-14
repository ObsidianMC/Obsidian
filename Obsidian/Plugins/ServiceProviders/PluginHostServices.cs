using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.Boss;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.WorldData;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Plugins.ServiceProviders;

/// <summary>
/// The host services a plugin is given, as instances of its own: <see cref="IServer"/> and
/// <see cref="ILevelFactory"/>, whose scoreboards, boss bars, recipes, server players and generators are the plugin's,
/// so that unloading it removes them. The rest is the server's services.
/// </summary>
/// <remarks>
/// <para>
/// The plugin is known from the instance it calls rather than from the code that calls it, so what it adds is its own
/// even when a library it uses, an async continuation or inlined code makes the call. The server's own code calls the
/// server's instances, so what it adds stays.
/// </para>
/// <para>
/// The services the plugin's <see cref="IServer"/> leads to are its own too: its command handler's
/// <see cref="ICommandHandler.ServiceProvider"/> is this, and scopes created from this resolve the same instances.
/// </para>
/// </remarks>
internal sealed class PluginHostServices : IServiceProvider, IServiceScopeFactory
{
    private readonly IServiceProvider serverServices;

    /// <param name="plugin">The plugin.</param>
    /// <param name="server">The server; the plugin's instance credits the plugin only when it's Obsidian's.</param>
    /// <param name="serverServices">The server's services.</param>
    public PluginHostServices(PluginContainer plugin, IServer server, IServiceProvider serverServices)
    {
        this.serverServices = serverServices;
        this.Server = server is Server concrete ? new PluginServer(concrete, plugin, this) : server;

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

    public object? GetService(Type serviceType) => this.Resolve(serviceType, this.serverServices, this);

    /// <summary>
    /// Creates a scope of the server's services that resolves the plugin's instances, like this.
    /// </summary>
    public IServiceScope CreateScope() => new Scope(this, this.serverServices.CreateAsyncScope());

    /// <summary>
    /// Adds the plugin's instances to its services, so that its own services get them too.
    /// </summary>
    public void AddTo(IServiceCollection services)
    {
        services.AddSingleton(this.Server);

        if (this.LevelFactory is not null)
            services.AddSingleton(this.LevelFactory);
    }

    // Resolves from the server's services, or a scope of them, with the plugin's instances in place of the server's.
    private object? Resolve(Type serviceType, IServiceProvider services, IServiceProvider self)
    {
        if (serviceType == typeof(IServiceProvider))
            return self;

        if (serviceType == typeof(IServiceScopeFactory))
            return this;

        if (serviceType == typeof(IServer))
            return this.Server;

        if (serviceType == typeof(ILevelFactory) && this.LevelFactory is not null)
            return this.LevelFactory;

        // GetServices<T>() asks for IEnumerable<T>: the plugin's instances stand in there too.
        if (serviceType == typeof(IEnumerable<IServer>))
            return new[] { this.Server };

        if (serviceType == typeof(IEnumerable<ILevelFactory>) && this.LevelFactory is not null)
            return new[] { this.LevelFactory };

        // The server's provider caches every type asked for, even ones it lacks, which would keep a plugin's types,
        // and so its load context, alive after it unloads.
        return PluginServiceProvider.IsCollectible(serviceType) ? null : services.GetService(serviceType);
    }

    // A scope of the server's services, which disposing ends.
    private sealed class Scope(PluginHostServices host, AsyncServiceScope scope)
        : IServiceScope, IServiceProvider, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) => host.Resolve(serviceType, scope.ServiceProvider, this);

        public void Dispose() => scope.Dispose();

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}

/// <summary>
/// A plugin's <see cref="ICommandHandler"/>: the server's, whose <see cref="ServiceProvider"/> is the plugin's host
/// services, and which registers the plugin's commands when given no plugin.
/// </summary>
internal sealed class PluginCommandHandler(ICommandHandler handler, PluginContainer plugin, IServiceProvider services)
    : ICommandHandler
{
    public IServiceProvider ServiceProvider => services;

    public Task ProcessCommand(CommandContext ctx) => handler.ProcessCommand(ctx);

    public Task<CommandCompletion> CompleteAsync(CommandContext ctx) => handler.CompleteAsync(ctx);

    // Given no plugin, the server's handler registers the server's commands, again and for no plugin to remove.
    public void RegisterCommands(IPluginContainer? pluginContainer = null) =>
        handler.RegisterCommands(pluginContainer ?? plugin);

    public bool IsValidArgumentType(Type type) => handler.IsValidArgumentType(type);

    public BaseArgumentParser GetArgumentParser(Type type) => handler.GetArgumentParser(type);

    public (int id, string mctype) FindMinecraftType(Type type) => handler.FindMinecraftType(type);

    public Command[] GetAllCommands() => handler.GetAllCommands();
}

/// <summary>
/// A plugin's <see cref="IEventDispatcher"/>: the server's, which registers the plugin's event handlers when given no
/// plugin.
/// </summary>
internal sealed class PluginEventDispatcher(IEventDispatcher dispatcher, PluginContainer plugin) : IEventDispatcher
{
    public ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs)
        where TEventArgs : BaseMinecraftEventArgs =>
        dispatcher.ExecuteEventAsync(eventArgs);

    // Given no plugin, the server's dispatcher registers the server's handlers, again and for no plugin to remove.
    public void RegisterEvents(IPluginContainer? pluginContainer = null) =>
        dispatcher.RegisterEvents(pluginContainer ?? plugin);

    public void Dispose() => dispatcher.Dispose();
}

/// <summary>
/// A plugin's <see cref="IServer"/>: the server, whose scoreboards, boss bars, recipes and server players created
/// through it are the plugin's, and whose command handler and event dispatcher are the plugin's views of the server's.
/// </summary>
/// <remarks>
/// The server hands it out in the plugin's calls too, where shared objects give the server: event arguments, command
/// and payload contexts, and connections (<see cref="IClient.Server"/>; see <see cref="IPluginScopedServer"/>). What
/// the server keeps for everyone, like its listed commands (<see cref="Command.CommandHandler"/>), holds the server's
/// instances.
/// </remarks>
/// <param name="server">The server.</param>
/// <param name="plugin">The plugin.</param>
/// <param name="services">The plugin's host services, which its command handler resolves from.</param>
internal sealed class PluginServer(Server server, PluginContainer plugin, IServiceProvider services)
    : IServer, IPluginScopedServer
{
    IServer IPluginScopedServer.ForCurrentCall() => ((IPluginScopedServer)server).ForCurrentCall();

    private readonly PluginCommandHandler commandHandler = new(server.CommandHandler, plugin, services);

    private readonly PluginEventDispatcher eventDispatcher = new(server.EventDispatcher, plugin);

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

    public ICommandHandler CommandHandler => this.commandHandler;

    /// <summary>
    /// Whether this is the plugin's instance of <paramref name="other"/>.
    /// </summary>
    public bool Wraps(Server other) => ReferenceEquals(server, other);

    /// <summary>
    /// The server's scoreboards, of which those this creates are the plugin's. Null until the server is ready.
    /// </summary>
    public IScoreboardManager ScoreboardManager =>
        server.ScoreboardManager is ScoreboardManager manager
            ? this.scoreboards ??= new PluginScoreboardManager(manager, plugin)
            : server.ScoreboardManager;

    public IEventDispatcher EventDispatcher => this.eventDispatcher;

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

    public TRecipe? FindRecipe<TRecipe>(RecipeType<TRecipe> type, IReadOnlyList<API.Inventory.ItemStack?> inputs)
        where TRecipe : class => server.FindRecipe(type, inputs);

    public bool AddPlayer(IPlayer player) => server.AddPlayer(player);

    public bool RemovePlayer(IPlayer player) => server.RemovePlayer(player);

    public Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null) =>
        server.AddServerPlayerAsync(uuid, username, world, plugin);

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
