using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Boss;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Services;
using Obsidian.WorldData;
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
    private readonly object shutdownLock = new();
    private Task? shutdownTask;
    private Task[] serverTasks = [];

    public IOptionsMonitor<WhitelistConfiguration> WhitelistConfiguration { get; }

    public ProtocolVersion Protocol => ServerConstants.DefaultProtocol;
    public int Tps { get; private set; }

    internal string TickStage { get; private set; } = "not started";
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
    internal static int ReserveEntityIds(int count) => Interlocked.Add(ref EntityCounter, count) - count + 1;

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
        if (this.Connections.TryGetValue(entityId, out var client) && client.Player is { } connectedPlayer &&
            OnlinePlayers.TryGetValue(connectedPlayer.Uuid, out var player))
            return player;

        return this.OnlinePlayers.Values.FirstOrDefault(player => player.EntityId == entityId);
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

        this.serverTasks = [
            LoopAsync(),
            KeepAliveLoopAsync(),
            ChunkLoopAsync(),
            ServerSaveAsync()
        ];

        // A failure here reaches the host, which reports the crash.
        try
        {
            // Polling leaves the cores to world generation instead of spinning one.
            while (!this.WorldManager.ReadyToJoin)
            {
                if (this.cancelTokenSource.IsCancellationRequested)
                    return;

                await Task.Delay(50);
            }

            if (this.cancelTokenSource.IsCancellationRequested)
                return;

            ScoreboardManager = new ScoreboardManager(this, this.loggerFactory);

            await this.PluginManager.OnServerReadyAsync();

            loadTimeStopwatch.Stop();
            Log.Ready(this.logger, loadTimeStopwatch.Elapsed, this.Port);

            await this.StartAsync(this.Port);

            await Task.WhenAll(this.serverTasks);
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

    public Task StopAsync()
    {
        lock (this.shutdownLock)
            return this.shutdownTask ??= this.StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        await cancelTokenSource.CancelAsync();

        this.socket?.Close();

        try
        {
            await Task.WhenAll(this.serverTasks);
        }
        finally
        {
            await WorldManager.DisposeAsync();
            await this.PluginManager.DisposeAsync();

            await this.userCache.SaveAsync();
        }
    }

    public bool AddPlayer(IPlayer player)
    {
        if (!this.OnlinePlayers.TryAdd(player.Uuid, player))
            return false;

        // The name must map to this player alone; undo the registration rather than leave lookups disagreeing.
        if (this.UsernameToUuidMappings.TryAdd(player.Username, player.Uuid))
            return true;

        this.OnlinePlayers.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player));
        return false;
    }

    public async Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null)
    {
        var player = new ServerPlayer(uuid, username, this, world ?? this.DefaultWorld);
        await player.LoadAsync();

        if (!this.AddPlayer(player))
            throw new InvalidOperationException($"Player '{username}' ({uuid}) is already online.");

        try
        {
            // The internal join handler registers the player in its level and fails the event if it can't.
            var result = await this.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, this, DateTimeOffset.Now));
            if (result != EventResult.Completed)
                throw new InvalidOperationException($"Joining server-side player '{username}' failed with result {result}.");

            return player;
        }
        catch
        {
            try
            {
                await MainEventHandler.DespawnPlayerAsync(player);
            }
            finally
            {
                this.RemovePlayer(player);
            }
            throw;
        }
    }

    public bool RemovePlayer(IPlayer player)
    {
        // Only the registered instance can unregister, so a stale reference can't remove a newer player who reused
        // the UUID or name.
        if (!this.OnlinePlayers.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player)))
            return false;

        this.ReleaseRegistrations(player);
        return true;
    }

    // Drops the lookups that hang off an online registration; the caller must have just removed that registration.
    private void ReleaseRegistrations(IPlayer player)
    {
        this.UsernameToUuidMappings.TryRemove(new KeyValuePair<string, Guid>(player.Username, player.Uuid));
        player.Level.TryRemovePlayer(player);
    }

    public async Task<bool> RemoveServerPlayerAsync(IPlayer player)
    {
        // Claiming the registration first means concurrent removals run the leave lifecycle only once.
        if (player is not ServerPlayer || !this.OnlinePlayers.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player)))
            return false;

        var result = EventResult.Failed;
        try
        {
            result = await this.EventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(player, this, DateTimeOffset.Now));
        }
        finally
        {
            try
            {
                // A failing handler stops the ones after it, which may include the internal one that despawns the player.
                if (result != EventResult.Completed)
                    await MainEventHandler.DespawnPlayerAsync(player);
            }
            finally
            {
                this.ReleaseRegistrations(player);
            }
        }

        return true;
    }

    // When the world tick in progress started (a Stopwatch timestamp), or 0 between ticks; see KeepAliveLoopAsync.
    private long tickStarted;

    // Failures of the world tick and of each connection loop, each logged now and then rather than on every tick.
    private readonly ThrottledFailures tickFailures = new();
    private readonly ThrottledFailures keepAliveFailures = new();
    private readonly ThrottledFailures chunkFailures = new();

    private static readonly TimeSpan StuckTickWarningAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StuckTickWarningInterval = TimeSpan.FromMinutes(1);

    private void ReportTickFailure(Exception exception)
    {
        if (this.tickFailures.ShouldLog(out var suppressed))
            Log.TickFailed(this.logger, exception, suppressed);
    }

    /// <summary>
    /// Keeps connections alive on its own timer, so a slow or stuck world tick doesn't also time every player out. It
    /// also warns while a world tick is stuck, with the stage each level is at. Chunks go out from
    /// <see cref="ChunkLoopAsync"/> instead, since a chunk read from disk can take long enough to delay keep-alives.
    /// </summary>
    private async Task KeepAliveLoopAsync()
    {
        var timer = new BalancingTimer(50, cancelTokenSource.Token);
        var keepAliveTicks = 0;
        var nextStuckWarning = StuckTickWarningAfter;
        var observedTick = 0L;

        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (++keepAliveTicks > Configuration.Network.KeepAliveInterval / 50)
                {
                    keepAliveTicks = 0;
                    await this.ForEachConnectionAsync(SendKeepAliveAsync, this.keepAliveFailures);
                }

                // Each tick gets its own warnings. Comparing start times also catches a tick that began right after
                // the previous one ended, between two checks, so it doesn't inherit that tick's later threshold.
                var started = Volatile.Read(ref this.tickStarted);
                if (started != observedTick)
                {
                    observedTick = started;
                    nextStuckWarning = StuckTickWarningAfter;
                }

                if (started == 0)
                    continue;

                var running = Stopwatch.GetElapsedTime(started);
                if (running >= nextStuckWarning)
                {
                    Log.TickStuck(this.logger, (int)running.TotalSeconds, this.DescribeLevelTicks());
                    nextStuckWarning = running + StuckTickWarningInterval;
                }
            }
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested)
        {
            // Stopping.
        }

        static ValueTask SendKeepAliveAsync(IClient client) => client.State switch
        {
            ClientState.Play => KeepAlivePacket.ClientboundPlay.HandleAsync(client),
            ClientState.Configuration => KeepAlivePacket.ClientboundConfiguration.HandleAsync(client),
            _ => ValueTask.CompletedTask
        };
    }

    /// <summary>
    /// Sends players their chunks on its own timer, so a slow or stuck world tick doesn't leave them without terrain.
    /// Chunks go out in batches the clients acknowledge (see <see cref="Player.SendPendingChunksAsync"/>).
    /// </summary>
    private async Task ChunkLoopAsync()
    {
        var timer = new BalancingTimer(50, cancelTokenSource.Token);

        try
        {
            while (await timer.WaitForNextTickAsync())
                await this.ForEachConnectionAsync(SendPendingChunksAsync, this.chunkFailures);
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested)
        {
            // Stopping.
        }

        static async ValueTask SendPendingChunksAsync(IClient client)
        {
            if (client.State == ClientState.Play && client.Player is Player player)
                await player.SendPendingChunksAsync();
        }
    }

    /// <summary>
    /// Runs <paramref name="service"/> for every connection. A client that fails (one closing its socket, say) is
    /// logged to <paramref name="failures"/> and skipped, so it can't hold up the clients after it.
    /// </summary>
    private async Task ForEachConnectionAsync(Func<IClient, ValueTask> service, ThrottledFailures failures)
    {
        foreach (var client in this.Connections.Values)
        {
            try
            {
                await service(client);
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException || !cancelTokenSource.IsCancellationRequested)
            {
                if (failures.ShouldLog(out var suppressed))
                    Log.ConnectionTickFailed(this.logger, ex, suppressed);
            }
        }
    }

    /// <summary>Logs a recurring failure at most every ten seconds, counting the occurrences in between.</summary>
    private sealed class ThrottledFailures
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
        private long lastLogged;
        private int suppressed;

        public bool ShouldLog(out int suppressedSinceLast)
        {
            suppressedSinceLast = this.suppressed;
            if (this.lastLogged != 0 && Stopwatch.GetElapsedTime(this.lastLogged) < Interval)
            {
                this.suppressed++;
                return false;
            }

            this.lastLogged = Stopwatch.GetTimestamp();
            this.suppressed = 0;
            return true;
        }
    }

    // Each level's tick stage, e.g. "overworld: ticking entities, the_nether: waiting for simulation gate".
    private string DescribeLevelTicks() => string.Join(", ", this.WorldManager.GetAvailableWorlds()
        .OfType<World>()
        .SelectMany(world => world.dimensions.Values.OfType<AbstractLevel>().Prepend(world))
        .Select(level => $"{level.Name}: {level.TickStage}"));

    private async Task ServerSaveAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            while (await timer.WaitForNextTickAsync(this.cancelTokenSource.Token))
            {
                Log.SavingWorlds(this.logger);
                try
                {
                    await WorldManager.FlushLoadedWorldsAsync();
                    await this.userCache.SaveAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "World autosave failed");
                }
            }
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested) { }
    }

    private async Task LoopAsync()
    {
        var tpsMeasure = new TpsMeasure();
        var stopwatch = Stopwatch.StartNew();
        var timer = new BalancingTimer(50, cancelTokenSource.Token);

        try
        {
            TickStage = "waiting for timer";
            while (await timer.WaitForNextTickAsync())
            {
                // Like vanilla, worlds tick once they're loaded: ticking chunks while the rest generate (fluids in complete
                // chunks) would change them before the world is ready.
                if (this.WorldManager.ReadyToJoin)
                {
                    TickStage = "ticking worlds";
                    Volatile.Write(ref this.tickStarted, Stopwatch.GetTimestamp());
                    try
                    {
                        await this.WorldManager.TickWorldsAsync();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancelTokenSource.IsCancellationRequested)
                    {
                        // A failing tick used to end this loop for good: players could still join, but nothing ticked
                        // again. The next tick runs anyway; repeated failures are only logged now and then.
                        this.ReportTickFailure(ex);
                    }
                    finally
                    {
                        Volatile.Write(ref this.tickStarted, 0);
                    }
                }

                long elapsedTicks = stopwatch.ElapsedTicks;
                stopwatch.Restart();
                tpsMeasure.PushMeasurement(elapsedTicks);
                Tps = tpsMeasure.Tps;
                TickStage = "waiting for timer";
            }
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested)
        {
            TickStage = "cancelled";
            // Just stop looping.
        }
        catch (Exception ex)
        {
            TickStage = $"failed: {ex.GetType().Name}: {ex.Message}";
            logger.LogError(ex, "The game tick loop failed");
            await this.cancelTokenSource.CancelAsync();
            throw;
        }

        TickStage = "stopped";
        foreach (var client in this.Connections.Values)
        {
            await client.DisconnectAsync("Server closed");
        }

        await WorldManager.FlushLoadedWorldsAsync();
    }

    public bool IsWhitelisted(string username) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => string.Equals(x.Name, username, StringComparison.OrdinalIgnoreCase));

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
        [LoggerMessage(Level = LogLevel.Error, Message = "A world tick failed ({Suppressed} more failures weren't logged since the last one)")]
        public static partial void TickFailed(ILogger logger, Exception exception, int suppressed);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The world tick has been running for {Seconds} s; levels: {Levels}")]
        public static partial void TickStuck(ILogger logger, int seconds, string levels);

        [LoggerMessage(Level = LogLevel.Error, Message = "Sending keep-alives or chunks failed ({Suppressed} more failures weren't logged since the last one)")]
        public static partial void ConnectionTickFailed(ILogger logger, Exception exception, int suppressed);

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
