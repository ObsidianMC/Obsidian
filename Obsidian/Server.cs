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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian;

public sealed partial class Server : IServer
{
    private static int EntityCounter = 0;

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    internal readonly CancellationTokenSource cancelTokenSource;
    internal readonly ILogger logger;

    public byte[] BrandData
    {
        get
        {
            var buffer = new NetworkBuffer();
            buffer.WriteString(this.Brand);

            return buffer.Data;
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
        this.DefaultWorld.PacketBroadcaster.QueuePacket(new SystemChatPacket(message, false));
        logger.LogInformation("{message}", message.Text);
    }

    public void BroadcastMessage(IWorld world, ChatMessage message)
    {
        this.DefaultWorld.PacketBroadcaster.QueuePacketToWorld(world, new SystemChatPacket(message, false));
        logger.LogInformation("{message}", message.Text);
    }

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        this.logger.LogInformation("SHA / Version: {VERSION}", ServerConstants.VERSION);

        this.logger.LogDebug("Registering events & commands...");

        this.CommandHandler.RegisterCommands();
        this.EventDispatcher.RegisterEvents();

        Directory.CreateDirectory(ServerConstants.PermissionPath);
        Directory.CreateDirectory(ServerConstants.PersistentDataPath);
        Directory.CreateDirectory(ServerConstants.AcceptedKeysPath);
        Directory.CreateDirectory("plugins");

        StartTime = DateTimeOffset.Now;
        this.Connections = new ConcurrentDictionary<int, IClient>(-1, this.MaxConnections);

        logger.LogInformation("Launching Obsidian Server v{Version}", this.Version);
        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Configuration.Network.MulitplayerDebugMode && Configuration.OnlineMode)
        {
            logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
            await StopAsync();
            return;
        }

        await RecipesRegistry.InitializeAsync();

        logger.LogInformation("Loading structures...");
        StructureRegistry.Initialize();

        await this.userCache.LoadAsync(this.cancelTokenSource.Token);

        logger.LogInformation("Loading properties...");

        await (Operators as OperatorList).InitializeAsync();

        logger.LogInformation("Loading plugins...");

        await PluginManager.LoadPluginsAsync();

        if (!Configuration.OnlineMode)
            logger.LogInformation("Starting in offline mode...");

        CommandsRegistry.Register(this);

        var serverTasks = new List<Task>()
        {
            LoopAsync(),
            ServerSaveAsync()
        };

        loadTimeStopwatch.Stop();
        logger.LogInformation("Server loaded in {time}", loadTimeStopwatch.Elapsed);

        //Wait for worlds to load
        while (!this.WorldManager.ReadyToJoin)
        {
            if (this.cancelTokenSource.IsCancellationRequested)
                return;

            continue;
        }

        ScoreboardManager = new ScoreboardManager(this, this.loggerFactory);

        await this.PluginManager.OnServerReadyAsync();

        logger.LogInformation("Listening for new clients...");

        await this.StartAsync(this.Port);

        try
        {
            await Task.WhenAll(serverTasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error has occurred");
            throw;
        }
        finally
        {
            // Try to shut the server down gracefully.
            await this.StopAsync();
            logger.LogInformation("The server has been shut down");
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

        logger.LogDebug("Saving worlds..");

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

        return this.OnlinePlayers.Remove(player.Uuid, out _);
    }

    private async Task ServerSaveAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            while (await timer.WaitForNextTickAsync(this.cancelTokenSource.Token))
            {
                logger.LogInformation("Saving world...");
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

        logger.LogInformation("The game loop has been stopped");
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
            this.logger.LogDebug("{ip} has been throttled for reconnecting too fast.", client.Ip!);
            await client.DisconnectAsync("Connection Throttled! Please wait before reconnecting.");
            return true;
        }

        return false;
    }
}
