using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net.Rcon;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Services;
using Obsidian.WorldData;
using System.Diagnostics;
using System.IO;
using System.Net;
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
    public const ProtocolVersion DefaultProtocol = ProtocolVersion.v1_21_4;

    public const string PersistentDataPath = "persistentdata";
    public const string PermissionPath = "permissions";

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    internal readonly CancellationTokenSource _cancelTokenSource;
    internal readonly ILogger _logger;

    internal byte[] BrandData
    {
        get
        {
            using var ms = new MinecraftStream();
            ms.WriteString(this.Brand);

            return ms.ToArray();
        }
    }

    private readonly ConcurrentQueue<ClientboundPacket> _chatMessagesQueue = new();
    private readonly ConcurrentHashSet<Client> _clients = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly RconServer _rconServer;
    private readonly IUserCache userCache;
    private readonly IServiceProvider serviceProvider;
    private readonly IDisposable? configWatcher;

    private IConnectionListener? _tcpListener;

    public IOptionsMonitor<WhitelistConfiguration> WhitelistConfiguration { get; }

    public ProtocolVersion Protocol => DefaultProtocol;

    public int Tps { get; private set; }
    public DateTimeOffset StartTime { get; private set; }

    public PluginManager PluginManager { get; }
    public EventDispatcher EventDispatcher { get; }

    public IOperatorList Operators { get; }
    public IScoreboardManager ScoreboardManager { get; private set; }
    public IWorldManager WorldManager { get; }

    public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new();

    public HashSet<string> RegisteredChannels { get; } = new();
    public CommandHandler CommandsHandler { get; }
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
        IWorldManager worldManager,
        RconServer rconServer,
        IUserCache playerCache,
        EventDispatcher eventDispatcher,
        CommandHandler commandHandler,
        IServiceProvider serviceProvider)
    {
        _logger = loggerFactory.CreateLogger<Server>();
        _logger.LogInformation("SHA / Version: {VERSION}", VERSION);
        _cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _cancelTokenSource.Token.Register(() => _logger.LogWarning("Obsidian is shutting down..."));
        _rconServer = rconServer;

        this.serviceProvider = serviceProvider;
        this.configWatcher = configuration.OnChange(this.ConfigChanged);

        var config = configuration.CurrentValue;

        Configuration = config;
        Port = config.Port;

        Operators = new OperatorList(this, loggerFactory);
        ScoreboardManager = new ScoreboardManager(this, loggerFactory);

        _logger.LogDebug(message: "Initializing command handler...");

        CommandsHandler = commandHandler;

        PluginManager = new PluginManager(this.serviceProvider, this, eventDispatcher, CommandsHandler, loggerFactory.CreateLogger<PluginManager>(),
            serviceProvider.GetRequiredService<IConfiguration>());

        _logger.LogDebug("Registering events & commands...");

        CommandsHandler.RegisterCommands();
        eventDispatcher.RegisterEvents();

        _logger.LogDebug("Done registering commands.");

        this.userCache = playerCache;
        this.EventDispatcher = eventDispatcher;
        this.WhitelistConfiguration = whitelistConfiguration;
        this.loggerFactory = loggerFactory;
        this.WorldManager = worldManager;

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
            AcceptClientsAsync(),
            LoopAsync(),
            ServerSaveAsync()
        };

        if (Configuration.EnableRcon)
            serverTasks.Add(_rconServer.RunAsync(this, _cancelTokenSource.Token));

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
            await HandleServerShutdown();
            _logger.LogInformation("The server has been shut down");
        }
    }

    private async Task HandleServerShutdown()
    {
        _logger.LogDebug("Flushing and disposing regions");
        await WorldManager.FlushLoadedWorldsAsync();
        await WorldManager.DisposeAsync();

        await this.userCache.SaveAsync();
    }

    private async Task AcceptClientsAsync()
    {
        _tcpListener = await SocketFactory.CreateListenerAsync(new IPEndPoint(IPAddress.Any, Port), token: _cancelTokenSource.Token);

        while (!_cancelTokenSource.Token.IsCancellationRequested)
        {
            ConnectionContext connection;
            try
            {
                var acceptedConnection = await _tcpListener.AcceptAsync(_cancelTokenSource.Token);
                if (acceptedConnection is null)
                {
                    // No longer accepting clients.
                    break;
                }
                connection = acceptedConnection;

                //TODO send a disconnect message
                if (!WorldManager.ReadyToJoin)
                {
                    connection.Abort();
                    await connection.DisposeAsync();

                    _logger.LogDebug("Server has not been fully initialized. Aborted the connection");
                    continue;
                }

            }
            catch (OperationCanceledException)
            {
                // No longer accepting clients.
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Listening for clients encountered an exception");
                break;
            }

            _logger.LogDebug("New connection from client with IP {ip}", connection.RemoteEndPoint);

            string ip = ((IPEndPoint)connection.RemoteEndPoint!).Address.ToString();

            if (Configuration.Whitelist && !WhitelistConfiguration.CurrentValue.WhitelistedIps.Contains(ip))
            {
                _logger.LogInformation("{ip} is not whitelisted. Closing connection", ip);
                connection.Abort();
                return;
            }

            if (this.Configuration.Network.ShouldThrottle)
            {
                if (throttler.TryGetValue(ip, out var time) && time <= DateTimeOffset.UtcNow)
                {
                    throttler.Remove(ip, out _);
                    _logger.LogDebug("Removed {ip} from throttler", ip);
                }
            }

            // TODO Entity ids need to be unique on the entire server, not per world
            var client = new Client(connection, this.loggerFactory, this.userCache, this);

            _clients.Add(client);
            _ = ExecuteAsync(client);
        }

        _logger.LogInformation("No longer accepting new clients");
        await _tcpListener.UnbindAsync();
        return;

        async Task ExecuteAsync(Client client)
        {
            await Task.Yield();

            try
            {
                await client.StartConnectionAsync();
            }
            catch (OperationCanceledException)
            {
                // Ignore.
            }
            catch (Exception exception)
            {
                _logger.LogError("Unexpected exception from client {Identifier}: {Message}", client.id, exception.Message);
            }
            finally
            {
                _clients.TryRemove(client);

                if (client.Player is not null)
                    _ = OnlinePlayers.TryRemove(client.Player.Uuid, out _);

                client.Dispose();
            }
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
        var context = new CommandContext(CommandHelpers.DefaultPrefix + input,
            new CommandSender(CommandIssuers.Console, null, _logger), null, this);

        try
        {
            await CommandsHandler.ProcessCommand(context);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{exceptionMessage}", e.Message);
        }
    }

    internal IEnumerable<Player> PlayersInRange(World world, Vector worldPosition)
    {
        var (x, z) = worldPosition.ToChunkCoord();

        var packedXZ = NumericsHelper.IntsToLong(x, z);
        return world.Players.Select(entry => entry.Value).Where(player => player.LoadedChunks.Contains(packedXZ));
    }

    internal void BroadcastBlockChange(World world, IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in PlayersInRange(world, location))
        {
            player.client.SendPacket(packet);
        }
    }

    internal void BroadcastBlockChange(World world, Player initiator, IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in PlayersInRange(world, location))
        {
            if (player == initiator)
                continue;

            player.client.SendPacket(packet);
        }
    }

    internal async Task HandleIncomingMessageAsync(ChatPacket packet, Client source, MessageType type = MessageType.Chat)
    {
        const string format = "<{0}> {1}";//TODO use this????
        var message = packet.Message;

        if (type is MessageType.Chat or MessageType.System)
        {
            await this.EventDispatcher.ExecuteEventAsync(new IncomingChatMessageEventArgs(source.Player, this, message, format));
        }
    }

    internal async Task QueueBroadcastPacketAsync(ClientboundPacket packet)
    {
        foreach (Player player in Players)
            await player.client.QueuePacketAsync(packet);
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

        if (_tcpListener is not null)
        {
            await _tcpListener.UnbindAsync();
        }

        foreach (var client in _clients)
        {
            client.Disconnect();
            client.Dispose();
        }
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
                    foreach (var client in _clients.Where(x => x.State == ClientState.Play || x.State == ClientState.Configuration))
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
                    foreach (Player player in Players)
                    {
                        player.client.SendPacket(packet);
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

        foreach (var client in _clients)
        {
            if (client.State == ClientState.Play)
                client.SendPacket(DisconnectPacket.ClientboundPlay with { Reason = ChatMessage.Simple("Server closed") });
            else if (client.State == ClientState.Configuration)
                client.SendPacket(DisconnectPacket.ClientboundConfiguration with { Reason = ChatMessage.Simple("Server closed") });
        }

        _logger.LogInformation("The game loop has been stopped");
        await WorldManager.FlushLoadedWorldsAsync();
    }
    
    public bool IsWhitedlisted(string username) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => x.Name == username);

    public bool IsWhitedlisted(Guid uuid) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => x.Id == uuid);

    public async ValueTask<bool> ShouldThrottleAsync(Client client)
    {
        if (!this.Configuration.Network.ShouldThrottle)
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        this.configWatcher?.Dispose();
    }
}
