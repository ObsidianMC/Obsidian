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
using System.IO;
using System.Reflection;
using System.Threading;

namespace Obsidian;

public sealed partial class Server : IServer
{
    private static int EntityCounter = 0;

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    internal readonly CancellationTokenSource _cancelTokenSource;
    internal readonly ILogger _logger;

    public byte[] BrandData
    {
        get
        {
            var buffer = new NetworkBuffer();
            buffer.WriteString(this.Brand);

            return buffer.Data;
        }
    }

    private readonly ILoggerFactory loggerFactory;
    private readonly IUserCache userCache;
    private readonly ServerMetrics serverMetrics;
    private readonly IServiceProvider serviceProvider;
    private readonly IDisposable? configWatcher;
    private readonly IPacketBroadcaster packetBroadcaster;

    public IOptionsMonitor<WhitelistConfiguration> WhitelistConfiguration { get; }

    public ProtocolVersion Protocol => ServerConstants.DefaultProtocol;
    public int Tps { get; private set; }
    public DateTimeOffset StartTime { get; private set; }

    public PluginManager PluginManager { get; }
    public IEventDispatcher EventDispatcher { get; }

    public IOperatorList Operators { get; }
    public IScoreboardManager ScoreboardManager { get; private set; }
    public IWorldManager WorldManager { get; }

    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers { get; } = new();

    public HashSet<string> RegisteredChannels { get; } = new();
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
        ServerMetrics serverMetrics,
        EventDispatcher eventDispatcher,
        IServiceProvider serviceProvider)
    {
        _logger = loggerFactory.CreateLogger<Server>();
        _logger.LogInformation("SHA / Version: {VERSION}", ServerConstants.VERSION);
        _cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _cancelTokenSource.Token.Register(() => _logger.LogWarning("Obsidian is shutting down..."));

        this.serviceProvider = serviceProvider;
        this.configWatcher = configuration.OnChange((config) =>
        {
            this.Configuration = config;
        });

        var config = configuration.CurrentValue;

        Configuration = config;
        Port = config.Port;

        Operators = new OperatorList(this, loggerFactory);
        ScoreboardManager = new ScoreboardManager(this, loggerFactory);

        _logger.LogDebug(message: "Initializing command handler...");

        CommandHandler = serviceProvider.GetRequiredService<CommandHandler>();

        PluginManager = ActivatorUtilities.CreateInstance<PluginManager>(this.serviceProvider, this);

        _logger.LogDebug("Registering events & commands...");

        CommandHandler.RegisterCommands();
        eventDispatcher.RegisterEvents();

        _logger.LogDebug("Done registering commands.");

        this.userCache = serviceProvider.GetRequiredService<IUserCache>();
        this.EventDispatcher = serviceProvider.GetRequiredService<EventDispatcher>();
        this.WhitelistConfiguration = whitelistConfiguration;
        this.serverMetrics = serverMetrics;
        this.loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        this.WorldManager = serviceProvider.GetRequiredService<IWorldManager>();
        this.packetBroadcaster = serviceProvider.GetRequiredService<IPacketBroadcaster>();

        Directory.CreateDirectory(ServerConstants.PermissionPath);
        Directory.CreateDirectory(ServerConstants.PersistentDataPath);
    }

    public static int GetNextEntityId() => Interlocked.Increment(ref EntityCounter);

    public void RegisterRecipes(params IRecipe[] recipes)
    {
        foreach (var recipe in recipes)
            RecipesRegistry.Recipes.Add(recipe.Identifier.ToSnakeCase(), recipe);
    }

    public bool IsPlayerOnline(string username) => OnlinePlayers.Values.Any(x => x.Username.EqualsIgnoreCase(username));

    public bool IsPlayerOnline(Guid uuid) => OnlinePlayers.ContainsKey(uuid);

    public IPlayer? GetPlayer(string username) => OnlinePlayers.Values.FirstOrDefault(player => player.Username.EqualsIgnoreCase(username));

    public IPlayer? GetPlayer(Guid uuid) => OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

    public IPlayer? GetPlayer(int entityId) => OnlinePlayers.Values.FirstOrDefault(player => player.EntityId == entityId);

    public bool TryGetPlayer(string username, out IPlayer? player)
    {
        if (this.GetPlayer(username) is IPlayer foundPlayer)
        {
            player = foundPlayer;
            return true;
        }

        player = null;
        return false;
    }

    public bool TryGetPlayer(Guid uuid, out IPlayer? player) => this.OnlinePlayers.TryGetValue(uuid, out player);

    public bool TryGetPlayer(int entityId, out IPlayer? player)
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
        this.packetBroadcaster.Broadcast(new SystemChatPacket(message, false));
        _logger.LogInformation("{message}", message.Text);
    }

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        StartTime = DateTimeOffset.Now;
        this.Connections = new ConcurrentDictionary<int, IClient>(-1, this.MaxConnections);

        _logger.LogInformation("Launching Obsidian Server v{Version}", this.Version);
        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Configuration.Network.MulitplayerDebugMode && Configuration.OnlineMode)
        {
            _logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
            await StopAsync();
            return;
        }

        await RecipesRegistry.InitializeAsync();

        _logger.LogInformation("Loading structures...");
        StructureRegistry.Initialize();

        await this.userCache.LoadAsync(this._cancelTokenSource.Token);

        _logger.LogInformation("Loading properties...");

        await (Operators as OperatorList).InitializeAsync();

        _logger.LogInformation("Loading plugins...");

        Directory.CreateDirectory("plugins");

        await PluginManager.LoadPluginsAsync();

        //await Task.WhenAll(Configuration.DownloadPlugins.Select(path => PluginManager.LoadPluginAsync(path)));

        if (!Configuration.OnlineMode)
            _logger.LogInformation("Starting in offline mode...");

        CommandsRegistry.Register(this);

        var serverTasks = new List<Task>()
        {
            LoopAsync(),
            ServerSaveAsync()
        };

        loadTimeStopwatch.Stop();
        _logger.LogInformation("Server loaded in {time}", loadTimeStopwatch.Elapsed);

        //Wait for worlds to load
        while (!this.WorldManager.ReadyToJoin)
        {
            if (this._cancelTokenSource.IsCancellationRequested)
                return;

            continue;
        }

        await this.PluginManager.OnServerReadyAsync();

        _logger.LogInformation("Listening for new clients...");

        await this.StartAsync(this.Port);

        try
        {
            await Task.WhenAll(serverTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error has occured");
            throw;
        }
        finally
        {
            // Try to shut the server down gracefully.
            await this.StopAsync();
            _logger.LogInformation("The server has been shut down");
        }
    }

    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags) => new BossBar(this)
    {
        Title = title,
        Health = health,
        Color = color,
        DivisionType = divisionType,
        Flags = flags
    };

    public async Task ExecuteCommand(string input)
    {
        var context = new CommandContext(CommandHelpers.DefaultPrefix + input, new CommandSender(CommandIssuers.Console, null), null, this);

        await CommandHandler.ProcessCommand(context);
    }

    public async Task StopAsync()
    {
        _cancelTokenSource.Cancel();

        this.socket.Close();

        foreach (var client in this.Connections.Values)
        {
            await client.DisconnectAsync("Server shutdown");
        }

        _logger.LogDebug("Flushing and disposing regions");
        await WorldManager.FlushLoadedWorldsAsync();
        await WorldManager.DisposeAsync();
        await this.PluginManager.DisposeAsync();

        await this.userCache.SaveAsync();
    }

    private async Task ServerSaveAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            while (await timer.WaitForNextTickAsync(this._cancelTokenSource.Token))
            {
                _logger.LogInformation("Saving world...");
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
        var timer = new BalancingTimer(50, _cancelTokenSource.Token);

        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                keepAliveTicks++;
                if (keepAliveTicks > (Configuration.Network.KeepAliveInterval / 50)) // to clarify: one tick is 50 milliseconds. 50 * 200 = 10000 millis means 10 seconds
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
            if (client.State == ClientState.Play)
                client.SendPacket(DisconnectPacket.ClientboundPlay with { Reason = ChatMessage.Simple("Server closed") });
            else if (client.State == ClientState.Configuration)
                client.SendPacket(DisconnectPacket.ClientboundConfiguration with { Reason = ChatMessage.Simple("Server closed") });
        }

        _logger.LogInformation("The game loop has been stopped");
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
            this._logger.LogDebug("{ip} has been throttled for reconnecting too fast.", client.Ip!);
            await client.DisconnectAsync("Connection Throttled! Please wait before reconnecting.");
            return true;
        }

        return false;
    }
}
