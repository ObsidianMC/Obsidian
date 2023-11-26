using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.Boss;
using Obsidian.API.Builders;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Hosting;
using Obsidian.Net;
using Obsidian.Net.Packets;
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
    public const ProtocolVersion DefaultProtocol = ProtocolVersion.v1_20_2;

    public const string PersistentDataPath = "persistentdata";
    public const string PermissionPath = "permissions";

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    internal readonly CancellationTokenSource _cancelTokenSource;

    private readonly ConcurrentQueue<IClientboundPacket> _chatMessagesQueue = new();
    private readonly ConcurrentHashSet<Client> _clients = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly RconServer _rconServer;
    private readonly IUserCache userCache;
    private readonly ILogger _logger;

    private IConnectionListener? _tcpListener;

    public ProtocolVersion Protocol => DefaultProtocol;

    public int Tps { get; private set; }
    public DateTimeOffset StartTime { get; private set; }

    public PluginManager PluginManager { get; }
    public MinecraftEventHandler Events { get; } = new();

    public IOperatorList Operators { get; }
    public IScoreboardManager ScoreboardManager { get; private set; }
    public IWorldManager WorldManager { get; }

    public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new();

    public HashSet<string> RegisteredChannels { get; } = new();
    public CommandHandler CommandsHandler { get; }
    public IServerConfiguration Configuration { get; }
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
        IServerEnvironment environment,
        ILoggerFactory loggerFactory,
        IWorldManager worldManager,
        RconServer rconServer,
        IUserCache playerCache)
    {
        Configuration = environment.Configuration;
        _logger = loggerFactory.CreateLogger<Server>();
        _logger.LogInformation("SHA / Version: {VERSION}", VERSION);
        _cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _cancelTokenSource.Token.Register(() => _logger.LogWarning("Obsidian is shutting down..."));
        _rconServer = rconServer;

        Port = Configuration.Port;

        Operators = new OperatorList(this, loggerFactory);
        ScoreboardManager = new ScoreboardManager(this, loggerFactory);

        _logger.LogDebug(message: "Initializing command handler...");
        CommandsHandler = new CommandHandler();

        PluginManager = new PluginManager(Events, this, _logger, CommandsHandler);
        CommandsHandler.LinkPluginManager(PluginManager);

        _logger.LogDebug("Registering commands...");
        CommandsHandler.RegisterCommandClass(null, new MainCommandModule());

        _logger.LogDebug("Registering custom argument parsers...");
        CommandsHandler.AddArgumentParser(new LocationTypeParser());
        CommandsHandler.AddArgumentParser(new PlayerTypeParser());

        _logger.LogDebug("Registering command context type...");
        _logger.LogDebug("Done registering commands.");

        this.userCache = playerCache;
        this.loggerFactory = loggerFactory;
        this.WorldManager = worldManager;

        Events.PlayerLeave += OnPlayerLeave;
        Events.PlayerJoin += OnPlayerJoin;
        Events.PlayerAttackEntity += PlayerAttack;
        Events.PlayerInteract += OnPlayerInteract;
        Events.ContainerClosed += OnContainerClosed;

        Directory.CreateDirectory(PermissionPath);
        Directory.CreateDirectory(PersistentDataPath);

        if (Configuration.AllowLan)
        {
            _ = Task.Run(async () =>
            {
                var udpClient = new UdpClient("224.0.2.60", 4445);
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
                string? lastMotd = null;
                byte[] bytes = []; // Cached motd as utf-8 bytes
                while (await timer.WaitForNextTickAsync(_cancelTokenSource.Token))
                {
                    if (Configuration.Motd != lastMotd)
                    {
                        lastMotd = Configuration.Motd;
                        bytes = Encoding.UTF8.GetBytes($"[MOTD]{Configuration.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{Configuration.Port}[/AD]");
                    }
                    await udpClient.SendAsync(bytes, bytes.Length);
                }
            });
        }
    }

    public void RegisterCommandClass<T>(PluginContainer plugin, T instance) =>
        CommandsHandler.RegisterCommandClass<T>(plugin, instance);

    public void RegisterArgumentHandler<T>(T parser) where T : BaseArgumentParser =>
        CommandsHandler.AddArgumentParser(parser);

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
        _chatMessagesQueue.Enqueue(new SystemChatMessagePacket(message, false));
        _logger.LogInformation(message.Text);
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(PlayerChatMessagePacket message)
    {
        _chatMessagesQueue.Enqueue(message);
        _logger.LogInformation("{}", message.Header.PlainMessage);
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(string message)
    {
        var chatMessage = ChatMessage.Simple(string.Empty)
            .AddExtra(message);

        _chatMessagesQueue.Enqueue(new SystemChatMessagePacket(chatMessage, false));
        _logger.LogInformation(message);
    }

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        StartTime = DateTimeOffset.Now;

        _logger.LogInformation("Launching Obsidian Server v{Version}", this.Version);
        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Configuration.MulitplayerDebugMode && Configuration.OnlineMode)
        {
            _logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
            await StopAsync();
            return;
        }

        await RecipesRegistry.InitializeAsync();

        await this.userCache.LoadAsync(this._cancelTokenSource.Token);

        _logger.LogInformation($"Loading properties...");

        await (Operators as OperatorList).InitializeAsync();

        _logger.LogInformation("Loading plugins...");

        Directory.CreateDirectory("plugins");

        PluginManager.DirectoryWatcher.Filters = new[] { ".cs", ".dll" };
        PluginManager.DirectoryWatcher.Watch("plugins");

        await Task.WhenAll(Configuration.DownloadPlugins.Select(path => PluginManager.LoadPluginAsync(path)));

        if (!Configuration.OnlineMode)
            _logger.LogInformation($"Starting in offline mode...");

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
        while (!this.WorldManager.ReadyToJoin && !this._cancelTokenSource.IsCancellationRequested)
            continue;

        _logger.LogInformation("Listening for new clients...");

        try
        {
            await Task.WhenAll(serverTasks);
        }
        catch (Exception)
        {
            // Maybe write a crash log to somewhere?
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

            if (Configuration.IpWhitelistEnabled && !Configuration.WhitelistedIPs.Contains(ip))
            {
                _logger.LogInformation("{ip} is not whitelisted. Closing connection", ip);
                connection.Abort();
                return;
            }

            if (this.Configuration.CanThrottle)
            {
                if (throttler.TryGetValue(ip, out var time) && time <= DateTimeOffset.UtcNow)
                {
                    throttler.Remove(ip, out _);
                    _logger.LogDebug("Removed {ip} from throttler", ip);
                }
            }

            // TODO Entity ids need to be unique on the entire server, not per world
            var client = new Client(connection, Math.Max(0, _clients.Count + this.DefaultWorld.GetTotalLoadedEntities()), this.loggerFactory, this.userCache, this);

            _clients.Add(client);

            client.Disconnected += client =>
            {
                _clients.TryRemove(client);

                if (client.Player is not null)
                    _ = OnlinePlayers.TryRemove(client.Player.Uuid, out _);
            };

            _ = Task.Run(client.StartConnectionAsync);
        }

        _logger.LogInformation("No longer accepting new clients");
        await _tcpListener.UnbindAsync();
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
        var context = new CommandContext(CommandsHandler._prefix + input, new CommandSender(CommandIssuers.Console, null, _logger), null, this);
        try
        {
            await CommandsHandler.ProcessCommand(context);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
        }
    }

    internal IEnumerable<Player> PlayersInRange(World world, Vector worldPosition) => world.Players.Select(entry => entry.Value).Where(player => player.client.LoadedChunks.Contains(worldPosition.ToChunkCoord()));

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

    internal async Task HandleIncomingMessageAsync(ChatMessagePacket packet, Client source, MessageType type = MessageType.Chat)
    {
        var format = "<{0}> {1}";
        var message = packet.Message;

        var chat = await Events.IncomingChatMessage.InvokeAsync(new IncomingChatMessageEventArgs(source.Player, this, message, format));
        if (chat.IsCancelled)
            return;

        //TODO add bool for sending secure chat messages
        ChatColor nameColor = source.Player.IsOperator ? ChatColor.BrightGreen : ChatColor.Gray;
        BroadcastMessage(ChatMessage.Simple(source.Player.Username, nameColor).AppendText($": {message}", ChatColor.White));
    }

    internal async Task QueueBroadcastPacketAsync(IClientboundPacket packet)
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
                await Events.ServerTick.InvokeAsync();

                keepAliveTicks++;
                if (keepAliveTicks > (Configuration.KeepAliveInterval / 50)) // to clarify: one tick is 50 milliseconds. 50 * 200 = 10000 millis means 10 seconds
                {
                    var keepAliveTime = DateTimeOffset.Now;

                    foreach (var client in _clients.Where(x => x.State == ClientState.Play || x.State == ClientState.Configuration))
                        client.SendKeepAlive(keepAliveTime);

                    keepAliveTicks = 0;
                }

                if (Configuration.Baah.HasValue)
                {
                    foreach (Player player in Players)
                    {
                        var soundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z);
                        await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.EntitySheepAmbient)
                            .WithSoundPosition(soundPosition)
                            .Build());
                    }
                }

                while (_chatMessagesQueue.TryDequeue(out IClientboundPacket packet))
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

                UpdateStatusConsole();
            }
        }
        catch (OperationCanceledException)
        {
            // Just stop looping.
        }

        _logger.LogInformation("The game loop has been stopped");
        await WorldManager.FlushLoadedWorldsAsync();
    }

    internal void UpdateStatusConsole()
    {
        var status = $"    tps:{Tps} c:{WorldManager.GeneratingChunkCount}/{WorldManager.LoadedChunkCount} r:{WorldManager.RegionCount}";
        ConsoleIO.UpdateStatusLine(status);
    }
}
