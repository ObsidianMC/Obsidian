using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Boss;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Rcon;
using Obsidian.Plugins;
using Obsidian.Services;
using Obsidian.WorldData;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Obsidian;

public sealed partial class Server : IServer
{
    private static int EntityCounter = 0;

#if RELEASE
    public const string VERSION = "0.1";
#else
    public static string VERSION
    {
        get
        {
            var informalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informalVersion != null && informalVersion.InformationalVersion.Contains('+'))
                return informalVersion.InformationalVersion.Split('+')[1];

            return "0.1";
        }
    }
#endif
    public const ProtocolVersion DefaultProtocol = ProtocolVersion.v1_21_6;
    public static readonly string ProtocolDescription = DefaultProtocol.GetDescription();


    public const string PersistentDataPath = "persistentdata";
    public const string PermissionPath = "permissions";

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

    private readonly ConcurrentQueue<ClientboundPacket> _chatMessagesQueue = new();
    private readonly ILoggerFactory loggerFactory;
    //TODO reimplement this
    //private readonly RconServer _rconServer;
    private readonly IUserCache userCache;
    private readonly ServerMetrics serverMetrics;
    private readonly IServiceProvider serviceProvider;
    private readonly IDisposable? configWatcher;

    public IOptionsMonitor<WhitelistConfiguration> WhitelistConfiguration { get; }

    public ProtocolVersion Protocol => DefaultProtocol;
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
    public string Version => VERSION;

    public string Brand { get; } = "obsidian";
    public int Port { get; }
    public IWorld DefaultWorld => WorldManager.DefaultWorld;
    public IEnumerable<IPlayer> Players => GetPlayers();

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
        _logger.LogInformation("SHA / Version: {VERSION}", VERSION);
        _cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _cancelTokenSource.Token.Register(() => _logger.LogWarning("Obsidian is shutting down..."));

        //_rconServer = serviceProvider.GetRequiredService<RconServer>();

        this.serviceProvider = serviceProvider;
        this.configWatcher = configuration.OnChange(this.ConfigChanged);

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

        Directory.CreateDirectory(PermissionPath);
        Directory.CreateDirectory(PersistentDataPath);

        //TODO turn this into a hosted service
        if (config.AllowLan)
        {
            _ = Task.Run(async () =>
            {
                var udpClient = new UdpClient("224.0.2.60", 4445);
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
                string? lastMotd = null;
                byte[] bytes = []; // Cached motd as utf-8 bytes
                while (await timer.WaitForNextTickAsync(_cancelTokenSource.Token))
                {
                    if (config.Motd != lastMotd)
                    {
                        lastMotd = config.Motd;
                        bytes = Encoding.UTF8.GetBytes($"[MOTD]{config.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{config.Port}[/AD]");
                    }
                    await udpClient.SendAsync(bytes, bytes.Length);
                }
            });
        }
    }

    private void ConfigChanged(ServerConfiguration configuration) => this.Configuration = configuration;

    // TODO make sure to re-send recipes
    public void RegisterRecipes(params IRecipe[] recipes)
    {
        foreach (var recipe in recipes)
            RecipesRegistry.Recipes.Add(recipe.Identifier.ToSnakeCase(), recipe);
    }

    /// <summary>
    /// Checks if a player is online.
    /// </summary>
    /// <param name="username">The username you want to check for.</param>
    /// <returns>True if the player is online.</returns>
    public bool IsPlayerOnline(string username) => OnlinePlayers.Any(x => x.Value.Username.EqualsIgnoreCase(username));

    public bool IsPlayerOnline(Guid uuid) => OnlinePlayers.ContainsKey(uuid);

    public IPlayer GetPlayer(string username) => OnlinePlayers.FirstOrDefault(player => player.Value.Username.EqualsIgnoreCase(username)).Value;

    public IPlayer? GetPlayer(Guid uuid) => OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

    public IPlayer GetPlayer(int entityId) => OnlinePlayers.FirstOrDefault(player => player.Value.EntityId == entityId).Value;

    private IEnumerable<IPlayer> GetPlayers()
    {
        foreach (var (_, player) in OnlinePlayers)
        {
            yield return player;
        }
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(ChatMessage message)
    {
        _chatMessagesQueue.Enqueue(new SystemChatPacket(message, false));
        _logger.LogInformation(message.Text);
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(PlayerChatPacket message)
    {
        _chatMessagesQueue.Enqueue(message);
        _logger.LogInformation("{}", message.UnsignedContent);
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(string message)
    {
        var chatMessage = ChatMessage.Simple(message);

        _chatMessagesQueue.Enqueue(new SystemChatPacket(chatMessage, false));
        _logger.LogInformation(message);
    }

    public static int GetNextEntityId() => Interlocked.Increment(ref EntityCounter);

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

        //if (Configuration.EnableRcon)
        //    serverTasks.Add(_rconServer.RunAsync(this, _cancelTokenSource.Token));

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

    internal IEnumerable<IPlayer> PlayersInRange(World world, Vector worldPosition)
    {
        var (x, z) = worldPosition.ToChunkCoord();

        var packedXZ = NumericsHelper.IntsToLong(x, z);
        return world.Players.Values.Where(player => player.LoadedChunks.Contains(packedXZ));
    }

    internal void BroadcastBlockChange(World world, IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in PlayersInRange(world, location))
        {
            player.Client.SendPacket(packet);
        }
    }

    internal void BroadcastBlockChange(World world, Player initiator, IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in PlayersInRange(world, location))
        {
            if (player == initiator)
                continue;

            player.Client.SendPacket(packet);
        }
    }

    internal async Task QueueBroadcastPacketAsync(ClientboundPacket packet)
    {
        foreach (Player player in Players)
            await player.Client.QueuePacketAsync(packet);
    }

    internal async Task DisconnectIfConnectedAsync(string username, ChatMessage? reason = null)
    {
        var player = Players.FirstOrDefault(x => x.Username == username);
        if (player != null)
        {
            reason ??= "Connected from another location";

            await player.KickAsync(reason);
        }
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

                if (Configuration.Baah.HasValue)
                {
                    foreach (Player player in Players)
                    {
                        var soundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z);
                        //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.EntitySheepAmbient)
                        //    .WithSoundPosition(soundPosition)
                        //    .Build());
                    }
                }

                while (_chatMessagesQueue.TryDequeue(out ClientboundPacket packet))
                {
                    foreach (var player in Players)
                    {
                        player.Client.SendPacket(packet);
                    }
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
