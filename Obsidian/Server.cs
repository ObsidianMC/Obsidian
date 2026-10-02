using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Boss;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.Commands.Framework;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Services;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Obsidian;

public sealed partial class Server : IServer
{
    private static int EntityCounter;

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    internal readonly CancellationTokenSource cancelTokenSource;
    internal readonly ILogger logger;

    public ReadOnlyMemory<byte> BrandData
    {
        get
        {
            var buffer = new NetworkBuffer();
            buffer.WriteString(this.Brand);

            return buffer.GetBuffer();
        }
    }

    private readonly IUserCache userCache;
    private readonly ILoggerFactory loggerFactory;
    private readonly IServiceProvider serviceProvider;
    private readonly IDisposable? configWatcher;

    public IOptionsMonitor<WhitelistConfiguration> WhitelistConfiguration { get; }

    public ProtocolVersion Protocol => ServerConstants.DefaultProtocol;
    public int Tps { get; private set; }
    public DateTimeOffset StartTime { get; private set; }

    public PluginManager PluginManager { get; }
    public IEventDispatcher EventDispatcher { get; }

    public IOperatorList Operators { get; }
    public IScoreboardManager ScoreboardManager { get; private set; }
    public IWorldManager WorldManager { get; }

    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers { get; } = [];
    private ConcurrentDictionary<string, Guid> UsernameToUuidMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RegisteredChannels { get; } = [];

    public ICommandHandler CommandHandler { get; }
    public ServerConfiguration Configuration { get; set; }
    public string Version => ServerConstants.VERSION;

    public string Brand { get; } = "obsidian";
    public int Port { get; }
    public IWorld DefaultWorld => WorldManager.DefaultWorld;

    /// <summary>
    /// Creates a new instance of <see cref="Server"/>.
    /// </summary>
    public Server(
        IHostApplicationLifetime lifetime,
        IOptionsMonitor<ServerConfiguration> configuration,
        IOptionsMonitor<WhitelistConfiguration> whitelistConfiguration,
        ILoggerFactory loggerFactory,
        EventDispatcher eventDispatcher,
        IServiceProvider serviceProvider,
        CommandHandler commandHandler,
        IUserCache userCache,
        IWorldManager worldManager)
    {
        this.logger = loggerFactory.CreateLogger<Server>();
        this.cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);

        this.serviceProvider = serviceProvider;
        this.WhitelistConfiguration = whitelistConfiguration;
        this.loggerFactory = loggerFactory;
        this.configWatcher = configuration.OnChange((config) =>
        {
            this.Configuration = config;
        });
        this.userCache = userCache;
        this.EventDispatcher = eventDispatcher;
        this.WorldManager = worldManager;

        var config = configuration.CurrentValue;

        this.Configuration = config;
        this.Port = config.Port;

        this.Operators = new OperatorList(this, loggerFactory);
        this.CommandHandler = commandHandler;
        this.PluginManager = ActivatorUtilities.CreateInstance<PluginManager>(this.serviceProvider, this);
    }

    public static int GetNextEntityId() => Interlocked.Increment(ref EntityCounter);

    public void RegisterRecipes(params IRecipe[] recipes)
    {
        foreach (var recipe in recipes)
            RecipesRegistry.Recipes.Add(recipe.Identifier.ToSnakeCase(), recipe);
    }

    public bool IsPlayerOnline(string username) => this.UsernameToUuidMappings.ContainsKey(username);

    public bool IsPlayerOnline(Guid uuid) => OnlinePlayers.ContainsKey(uuid);

    public IPlayer? GetPlayer(string username)
    {
        if (this.UsernameToUuidMappings.TryGetValue(username, out var uuid) && OnlinePlayers.TryGetValue(uuid, out var player))
            return player;

        return null;
    }

    public IPlayer? GetPlayer(Guid uuid) => OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

    public IPlayer? GetPlayer(int entityId)
    {
        if (this.Connections.TryGetValue(entityId, out var client) && OnlinePlayers.TryGetValue(client.Player!.Uuid, out var player))
            return player;

        return null;
    }

    public bool TryGetPlayer(string username, [NotNullWhen(true)] out IPlayer? player)
    {
        if (this.GetPlayer(username) is IPlayer foundPlayer)
        {
            player = foundPlayer;
            return true;
        }

        player = null;
        return false;
    }

    
    public bool TryGetPlayer(Guid uuid, [NotNullWhen(true)] out IPlayer? player) => this.OnlinePlayers.TryGetValue(uuid, out player);

    public bool TryGetPlayer(int entityId, [NotNullWhen(true)] out IPlayer? player)
    {
        if (this.GetPlayer(entityId) is IPlayer foundPlayer)
        {
            player = foundPlayer;
            return true;
        }

        player = null;
        return false;
    }

    public void BroadcastMessage(ChatMessage message)
    {
        this.DefaultWorld.PacketBroadcaster.QueuePacket(new SystemChatPacket(message, false));
        Log.Broadcast(this.logger, message.Text);
    }

    public void BroadcastMessage(IWorld world, ChatMessage message)
    {
        this.DefaultWorld.PacketBroadcaster.QueuePacketToLevel(world, new SystemChatPacket(message, false));
        Log.Broadcast(this.logger, message.Text);
    }

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        Log.Starting(this.logger, this.Version);

        this.CommandHandler.RegisterCommands();
        this.EventDispatcher.RegisterEvents();

        Directory.CreateDirectory(ServerConstants.PermissionPath);
        Directory.CreateDirectory(ServerConstants.PersistentDataPath);
        Directory.CreateDirectory(ServerConstants.AcceptedKeysPath);
        Directory.CreateDirectory(ServerConstants.PluginsPath);

        StartTime = DateTimeOffset.Now;
        this.Connections = new ConcurrentDictionary<int, IClient>(-1, this.MaxConnections);

        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Configuration.Network.MulitplayerDebugMode && Configuration.OnlineMode)
        {
            Log.IncompatibleDebugMode(this.logger);
            await StopAsync();
            return;
        }

        await RecipesRegistry.InitializeAsync();

        await this.userCache.LoadAsync(this.cancelTokenSource.Token);

        await (Operators as OperatorList).InitializeAsync();

        await PluginManager.LoadPluginsAsync();

        if (!Configuration.OnlineMode)
            Log.OfflineMode(this.logger);

        CommandsRegistry.Register(this);

        var serverTasks = new List<Task>()
        {
            LoopAsync(),
            ServerSaveAsync()
        };

        // Wait for worlds to load. Polling with a delay leaves the cores to world generation instead of spinning one.
        while (!this.WorldManager.ReadyToJoin)
        {
            if (this.cancelTokenSource.IsCancellationRequested)
                return;

            await Task.Delay(50);
        }

        ScoreboardManager = new ScoreboardManager(this, this.loggerFactory);

        await this.PluginManager.OnServerReadyAsync();

        loadTimeStopwatch.Stop();
        Log.Ready(this.logger, loadTimeStopwatch.Elapsed, this.Port);

        await this.StartAsync(this.Port);

        // A failure here reaches the host, which reports the crash.
        try
        {
            await Task.WhenAll(serverTasks);
        }
        finally
        {
            // Try to shut the server down gracefully.
            await this.StopAsync();
            Log.Stopped(this.logger);
        }
    }

    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags) =>
        ActivatorUtilities.CreateInstance<BossBar>(this.serviceProvider, title, health, color, divisionType, flags);

    public async Task ExecuteCommand(string input)
    {
        var context = new CommandContext(CommandHelpers.DefaultPrefix + input, new CommandSender(CommandIssuers.Console, null), null, this);

        await CommandHandler.ProcessCommand(context);
    }

    public async Task StopAsync()
    {
        cancelTokenSource.Cancel();

        this.socket.Close();

        await WorldManager.FlushLoadedWorldsAsync();
        await WorldManager.DisposeAsync();
        await this.PluginManager.DisposeAsync();

        await this.userCache.SaveAsync();
    }

    public bool AddPlayer(IPlayer player)
    {
        this.UsernameToUuidMappings.TryAdd(player.Username, player.Uuid);

        return this.OnlinePlayers.TryAdd(player.Uuid, player);
    }

    public bool RemovePlayer(IPlayer player)
    {
        this.UsernameToUuidMappings.Remove(player.Username, out _);

        player.Level.TryRemovePlayer(player);

        return this.OnlinePlayers.Remove(player.Uuid, out _);
    }

    private async Task ServerSaveAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            while (await timer.WaitForNextTickAsync(this.cancelTokenSource.Token))
            {
                Log.SavingWorlds(this.logger);
                await WorldManager.FlushLoadedWorldsAsync();
                await this.userCache.SaveAsync();
            }
        }
        catch { }
    }

    private async Task LoopAsync()
    {
        var keepAliveTicks = 0;

        var tpsMeasure = new TpsMeasure();
        var stopwatch = Stopwatch.StartNew();
        var timer = new BalancingTimer(50, cancelTokenSource.Token);
        var keepAliveInterval = Configuration.Network.KeepAliveInterval / 50;

        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (keepAliveInterval != Configuration.Network.KeepAliveInterval / 50)
                    keepAliveInterval = Configuration.Network.KeepAliveInterval / 50;

                keepAliveTicks++;
                if (keepAliveTicks > keepAliveInterval)
                {
                    foreach (var client in this.Connections.Values.Where(x => x.State == ClientState.Play || x.State == ClientState.Configuration))
                    {
                        if (client.State == ClientState.Play)
                            await KeepAlivePacket.ClientboundPlay.HandleAsync(client);
                        else
                            await KeepAlivePacket.ClientboundConfiguration.HandleAsync(client);
                    }

                    keepAliveTicks = 0;
                }

                // Like vanilla, worlds tick once they're loaded: ticking chunks while the rest generate (fluids in complete
                // chunks) would change them before the world is ready.
                if (this.WorldManager.ReadyToJoin)
                    await this.WorldManager.TickWorldsAsync();

                long elapsedTicks = stopwatch.ElapsedTicks;
                stopwatch.Restart();
                tpsMeasure.PushMeasurement(elapsedTicks);
                Tps = tpsMeasure.Tps;
            }
        }
        catch (OperationCanceledException)
        {
            // Just stop looping.
        }

        foreach (var client in this.Connections.Values)
        {
            await client.DisconnectAsync("Server closed");
        }

        await WorldManager.FlushLoadedWorldsAsync();
    }

    public bool IsWhitelisted(string username) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => x.Name == username);

    public bool IsWhitelisted(Guid uuid) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => x.Id == uuid);

    public async ValueTask<bool> ShouldThrottleAsync(Client client)
    {
        if (!this.Configuration.Network.ShouldThrottle)
            return false;

        if (!client.Connected)
            return false;

        if (!throttler.TryGetValue(client.Ip!, out var timeLeft))
        {
            throttler.TryAdd(client.Ip!, DateTimeOffset.UtcNow.AddMilliseconds(this.Configuration.Network.ConnectionThrottle));
            return false;
        }

        if (DateTimeOffset.UtcNow < timeLeft)
        {
            Log.Throttled(this.logger, client.Ip!);
            await client.DisconnectAsync("Connection Throttled! Please wait before reconnecting.");
            return true;
        }

        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Starting Obsidian {Version}")]
        public static partial void Starting(ILogger logger, string version);

        [LoggerMessage(Level = LogLevel.Error, Message = "Multiplayer debug mode can't be enabled together with online mode, since it overwrites usernames")]
        public static partial void IncompatibleDebugMode(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Running in offline mode; player identities are not verified")]
        public static partial void OfflineMode(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Server ready in {Elapsed}, listening on port {Port}")]
        public static partial void Ready(ILogger logger, TimeSpan elapsed, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Server stopped")]
        public static partial void Stopped(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Message}")]
        public static partial void Broadcast(ILogger logger, string message);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Saving worlds")]
        public static partial void SavingWorlds(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Throttled {Ip} for reconnecting too quickly")]
        public static partial void Throttled(ILogger logger, string ip);

        [LoggerMessage(Level = LogLevel.Error, Message = "Accepting a connection failed with socket error {SocketError}")]
        public static partial void AcceptFailed(ILogger logger, System.Net.Sockets.SocketError socketError);

        [LoggerMessage(Level = LogLevel.Information, Message = "Rejected {Ip}: not whitelisted")]
        public static partial void NotWhitelisted(ILogger logger, string ip);
    }
}
