using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Boss;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Integrated;
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

    private readonly ConcurrentDictionary<Task, byte> pendingLeaves = new();

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

    /// <summary>
    /// The port the server listens on: the configured one until it's started, then the bound one.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Whether the worlds are paused, like an integrated server while its game is paused: they don't tick, and the ticks
    /// don't count toward the autosave, while connections keep running.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// Raised when a save of everything starts, with whether it's an autosave.
    /// </summary>
    public event Action<bool>? SaveStarted;

    /// <summary>
    /// Raised when a save of everything ends, with whether it was an autosave and its failure, if it failed.
    /// </summary>
    public event Action<bool, Exception?>? SaveCompleted;

    /// <summary>
    /// The integrated server's session, when a game client runs this server.
    /// </summary>
    internal IntegratedSession? Integrated { get; }

    // Vanilla's autosave interval (MinecraftServer's autosave period): every 6000 ticks, 5 minutes at 20 TPS.
    private const int AutosaveInterval = 6000;

    // Saves of everything run one at a time.
    private readonly SemaphoreSlim saveLock = new(1, 1);
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
        this.Integrated = serviceProvider.GetService<IntegratedSession>();

        this.Operators = new OperatorList(this, loggerFactory, this.Integrated);
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

        if (this.Integrated is not null)
            ((CommandHandler)this.CommandHandler).RegisterCommandClass(null, typeof(IntegratedCommandModule));

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

        var loop = LoopAsync();

        // Wait for worlds to load. Polling with a delay leaves the cores to world generation instead of spinning one.
        while (!this.WorldManager.ReadyToJoin)
        {
            if (this.cancelTokenSource.IsCancellationRequested)
                return;

            await Task.Delay(50);
        }

        ScoreboardManager = new ScoreboardManager(this, this.loggerFactory);

        await this.PluginManager.OnServerReadyAsync();

        await this.StartAsync(this.Port);

        loadTimeStopwatch.Stop();
        Log.Ready(this.logger, loadTimeStopwatch.Elapsed, this.Port);

        // A failure here reaches the host, which reports the crash.
        try
        {
            await loop;
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
        await cancelTokenSource.CancelAsync();

        this.CloseListeners();

        await this.PendingLeavesAsync();
        await WorldManager.FlushLoadedWorldsAsync();
        await WorldManager.DisposeAsync();
        await this.PluginManager.DisposeAsync();

        await this.userCache.SaveAsync();
    }

    /// <summary>
    /// Saves everything: the online players, the worlds with their regions and level data, and the user cache. Saves run
    /// one at a time; a failed one is logged and reported through <see cref="SaveCompleted"/>.
    /// </summary>
    /// <param name="autosave">Whether this is the periodic autosave, rather than one that was asked for.</param>
    /// <returns>Whether the save succeeded.</returns>
    public async Task<bool> SaveEverythingAsync(bool autosave)
    {
        await this.saveLock.WaitAsync();
        try
        {
            Log.SavingWorlds(this.logger);
            this.SaveStarted?.Invoke(autosave);

            try
            {
                foreach (var player in this.OnlinePlayers.Values)
                    await player.SaveAsync();

                await WorldManager.FlushLoadedWorldsAsync();
                await this.userCache.SaveAsync();
            }
            catch (Exception ex)
            {
                Log.AutosaveFailed(this.logger, ex);
                this.SaveCompleted?.Invoke(autosave, ex);
                return false;
            }

            this.SaveCompleted?.Invoke(autosave, null);
            return true;
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    public bool AddPlayer(IPlayer player)
    {
        this.UsernameToUuidMappings.TryAdd(player.Username, player.Uuid);

        return this.OnlinePlayers.TryAdd(player.Uuid, player);
    }

    /// <summary>
    /// Keeps track of a player leaving after their connection closed, whose save stopping the server waits for.
    /// </summary>
    internal void TrackLeave(Task leave)
    {
        if (leave.IsCompleted)
            return;

        this.pendingLeaves.TryAdd(leave, 0);
        _ = leave.ContinueWith(finished => this.pendingLeaves.TryRemove(finished, out _), TaskScheduler.Default);
    }

    // Leaves log their own failures, so waiting for them never throws.
    private Task PendingLeavesAsync() => Task.WhenAll(this.pendingLeaves.Keys);

    public bool RemovePlayer(IPlayer player)
    {
        this.UsernameToUuidMappings.Remove(player.Username, out _);

        player.Level.TryRemovePlayer(player);

        return this.OnlinePlayers.Remove(player.Uuid, out _);
    }

    /// <summary>
    /// The mean time the last 100 world ticks took to run, in milliseconds, like vanilla's smoothed tick time (the busy
    /// time of a tick, not its 50 ms interval).
    /// </summary>
    public double AverageTickMilliseconds
    {
        get
        {
            lock (this.tickTimes)
                return this.tickTimeCount == 0 ? 0 : this.tickTimes.Take(this.tickTimeCount).Average();
        }
    }

    private readonly double[] tickTimes = new double[100];
    private int tickTimeCount;
    private int tickTimeIndex;

    private void RecordTickTime(TimeSpan elapsed)
    {
        lock (this.tickTimes)
        {
            this.tickTimes[this.tickTimeIndex] = elapsed.TotalMilliseconds;
            this.tickTimeIndex = (this.tickTimeIndex + 1) % this.tickTimes.Length;
            this.tickTimeCount = Math.Min(this.tickTimeCount + 1, this.tickTimes.Length);
        }
    }

    private async Task LoopAsync()
    {
        var keepAliveTicks = 0;
        var worldTicks = 0;
        Task autosave = Task.CompletedTask;

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
                if (this.WorldManager.ReadyToJoin && !this.Paused)
                {
                    var tickStart = Stopwatch.GetTimestamp();
                    await this.WorldManager.TickWorldsAsync();
                    this.RecordTickTime(Stopwatch.GetElapsedTime(tickStart));

                    // The autosave runs beside the ticks; when the previous one is still running, this one is skipped.
                    if (++worldTicks % AutosaveInterval == 0 && autosave.IsCompleted)
                        autosave = this.SaveEverythingAsync(autosave: true);
                }

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

        await autosave;

        foreach (var client in this.Connections.Values)
        {
            await client.DisconnectAsync("Server closed");
        }

        await this.PendingLeavesAsync();
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

        [LoggerMessage(Level = LogLevel.Error, Message = "Saving the worlds failed")]
        public static partial void AutosaveFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Throttled {Ip} for reconnecting too quickly")]
        public static partial void Throttled(ILogger logger, string ip);

        [LoggerMessage(Level = LogLevel.Error, Message = "Accepting a connection failed with socket error {SocketError}")]
        public static partial void AcceptFailed(ILogger logger, System.Net.Sockets.SocketError socketError);

        [LoggerMessage(Level = LogLevel.Information, Message = "Rejected {Ip}: not whitelisted")]
        public static partial void NotWhitelisted(ILogger logger, string ip);
    }
}
