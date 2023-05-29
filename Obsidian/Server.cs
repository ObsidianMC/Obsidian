using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.Boss;
using Obsidian.API.Builders;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Hosting;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net.Rcon;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Obsidian;

public partial class Server : IServer
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
    public const ProtocolVersion DefaultProtocol = ProtocolVersion.v1_19_4;

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    internal readonly CancellationTokenSource _cancelTokenSource;
    internal string PermissionPath => Path.Combine(ServerFolderPath, "permissions");

    private readonly ConcurrentQueue<IClientboundPacket> _chatMessagesQueue = new();
    private readonly ConcurrentHashSet<Client> _clients = new();
    private readonly TcpListener _tcpListener;
    private readonly RconServer _rconServer;
    private readonly ILogger _logger;

    public ProtocolVersion Protocol => DefaultProtocol;

    public int Tps { get; private set; }
    public DateTimeOffset StartTime { get; private set; }

    public PluginManager PluginManager { get; }
    public MinecraftEventHandler Events { get; } = new();

    public IOperatorList Operators { get; }
    public IScoreboardManager ScoreboardManager { get; private set; }

    public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new();
    public Dictionary<string, Type> WorldGenerators { get; } = new();

    public HashSet<string> RegisteredChannels { get; } = new();
    public CommandHandler CommandsHandler { get; }

    public ServerConfiguration Config { get; }
    public IServerConfiguration Configuration => Config;
    public string Version => VERSION;
    public string ServerFolderPath { get; }
    public string PersistentDataPath { get; }
    public string Brand { get; } = "obsidian";
    public int Port { get; }
    public WorldManager WorldManager { get; private set; }
    public IWorld DefaultWorld => WorldManager.DefaultWorld;
    public IEnumerable<IPlayer> Players => GetPlayers();

    // TODO: This should be removed. Services should get their own logger.
    public ILogger Logger => _logger;

    /// <summary>
    /// Creates a new instance of <see cref="Server"/>.
    /// </summary>
    public Server(
        IHostApplicationLifetime lifetime,
        IServerEnvironment environment,
        ILogger<Server> logger,
        RconServer rconServer)
    {
        _logger = logger;
        _logger.LogInformation($"SHA / Version: {VERSION}");
        _cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _cancelTokenSource.Token.Register(() => _logger.LogWarning("Obsidian is shutting down..."));
        _rconServer = rconServer;

        Config = environment.Configuration;
        Port = Config.Port;
        ServerFolderPath = Directory.GetCurrentDirectory();

        _tcpListener = new TcpListener(IPAddress.Any, Port);

        Operators = new OperatorList(this);

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

        WorldManager = new WorldManager(this, _logger, environment.ServerWorlds);

        Events.PlayerLeave += OnPlayerLeave;
        Events.PlayerJoin += OnPlayerJoin;
        Events.PlayerAttackEntity += PlayerAttack;
        Events.PlayerInteract += OnPlayerInteract;
        Events.ContainerClosed += OnContainerClosed;

        PersistentDataPath = Path.Combine(ServerFolderPath, "persistentdata");

        Directory.CreateDirectory(PermissionPath);
        Directory.CreateDirectory(PersistentDataPath);

        if (Config.UDPBroadcast)
        {
            _ = Task.Run(async () =>
            {
                var udpClient = new UdpClient("224.0.2.60", 4445);
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));
                string? lastMotd = null;
                byte[] bytes = Array.Empty<byte>(); // Cached motd as utf-8 bytes
                while (await timer.WaitForNextTickAsync(_cancelTokenSource.Token))
                {
                    if (Config.Motd != lastMotd)
                    {
                        lastMotd = Config.Motd;
                        bytes = Encoding.UTF8.GetBytes($"[MOTD]{Config.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{Config.Port}[/AD]");
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
    /// Registers new world generator(s) to the server.
    /// </summary>
    /// <param name="entries">A compatible list of entries.</param>
    public void RegisterWorldGenerator<T>() where T : IWorldGenerator, new()
    {
        var gen = new T();
        if (string.IsNullOrWhiteSpace(gen.Id))
            throw new InvalidOperationException($"Failed to get id for generator: {gen.Id}");

        if (this.WorldGenerators.TryAdd(gen.Id, typeof(T)))
            this._logger.LogDebug($"Registered {gen.Id}...");
    }

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        StartTime = DateTimeOffset.Now;

        _logger.LogInformation($"Launching Obsidian Server v{Version}");
        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Config.MulitplayerDebugMode && Config.OnlineMode)
        {
            _logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
            Stop();
            return;
        }

        await RecipesRegistry.InitializeAsync();

        await UserCache.LoadAsync(this._cancelTokenSource.Token);

        _logger.LogInformation($"Loading properties...");

        await (Operators as OperatorList).InitializeAsync();
        RegisterDefaults();

        ScoreboardManager = new ScoreboardManager(this);
        _logger.LogInformation("Loading plugins...");

        Directory.CreateDirectory(Path.Join(ServerFolderPath, "plugins"));

        PluginManager.DirectoryWatcher.Filters = new[] { ".cs", ".dll" };
        PluginManager.DirectoryWatcher.Watch(Path.Join(ServerFolderPath, "plugins"));

        await Task.WhenAll(Config.DownloadPlugins.Select(path => PluginManager.LoadPluginAsync(path)));

        // TODO: This should defenitly accept a cancellation token.
        // If Cancel is called, this method should stop within the configured timeout, otherwise code execution will simply stop here,
        // and server shutdown will not be handled correctly.
        await WorldManager.LoadWorldsAsync();

        if (!Config.OnlineMode)
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

        await UserCache.SaveAsync();
    }

    private async Task AcceptClientsAsync()
    {
        _tcpListener.Start();

        while (!_cancelTokenSource.Token.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _tcpListener.AcceptSocketAsync(_cancelTokenSource.Token);
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

            _logger.LogDebug("New connection from client with IP {ip}", socket.RemoteEndPoint);

            string ip = ((IPEndPoint)socket.RemoteEndPoint!).Address.ToString();

            if (Config.IpWhitelistEnabled && !Config.WhitelistedIPs.Contains(ip))
            {
                _logger.LogInformation("{ip} is not whitelisted. Closing connection", ip);
                socket.Disconnect(false);
                return;
            }

            if (this.Config.CanThrottle)
            {
                if (throttler.TryGetValue(ip, out var time) && time <= DateTimeOffset.UtcNow)
                {
                    throttler.Remove(ip, out _);
                    this.Logger.LogDebug("Removed {ip} from throttler", ip);
                }
            }

            // TODO Entity ids need to be unique on the entire server, not per world
            var client = new Client(socket, Config, Math.Max(0, _clients.Count + WorldManager.DefaultWorld.GetTotalLoadedEntities()), this);

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
        _tcpListener.Stop();
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

        var chat = await Events.InvokeIncomingChatMessageAsync(new IncomingChatMessageEventArgs(source.Player, message, format));
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

    internal async Task QueueBroadcastPacketAsync(IClientboundPacket packet, params int[] excluded)
    {
        foreach (Player player in Players.Where(x => !excluded.Contains(x.EntityId)))
            await player.client.QueuePacketAsync(packet);
    }

    internal void BroadcastPacket(IClientboundPacket packet)
    {
        foreach (Player player in Players)
        {
            player.client.SendPacket(packet);
        }
    }

    internal void BroadcastPacket(IClientboundPacket packet, params int[] excluded)
    {
        foreach (Player player in Players.Where(x => !excluded.Contains(x.EntityId)))
            player.client.SendPacket(packet);
    }

    internal async Task BroadcastNewCommandsAsync()
    {
        CommandsRegistry.Register(this);
        foreach (Player player in Players)
            await player.client.SendCommandsAsync();
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

    private bool TryAddEntity(World world, Entity entity) => world.TryAddEntity(entity);

    internal async Task BroadcastPlayerDigAsync(PlayerDiggingStore store, IBlock block)
    {
        var digging = store.Packet;

        var player = OnlinePlayers.GetValueOrDefault(store.Player);

        switch (digging.Status)
        {
            case DiggingStatus.DropItem:
                {
                    var droppedItem = player.GetHeldItem();

                    if (droppedItem is null or { Type: Material.Air })
                        return;

                    var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

                    var item = new ItemEntity
                    {
                        EntityId = player + player.world.GetTotalLoadedEntities() + 1,
                        Count = 1,
                        Id = droppedItem.AsItem().Id,
                        Glowing = true,
                        World = player.world,
                        Server = player.Server,
                        Position = loc
                    };

                    TryAddEntity(player.world, item);

                    var lookDir = player.GetLookDirection();

                    var vel = Velocity.FromDirection(loc, lookDir);//TODO properly shoot the item towards the direction the players looking at

                    BroadcastPacket(new SpawnEntityPacket
                    {
                        EntityId = item.EntityId,
                        Uuid = item.Uuid,
                        Type = EntityType.Item,
                        Position = item.Position,
                        Pitch = 0,
                        Yaw = 0,
                        Data = 1,
                        Velocity = vel
                    });
                    BroadcastPacket(new SetEntityMetadataPacket
                    {
                        EntityId = item.EntityId,
                        Entity = item
                    });

                    player.Inventory.RemoveItem(player.inventorySlot, player.Sneaking ? 64 : 1);//TODO get max stack size for the item

                    player.client.SendPacket(new SetContainerSlotPacket
                    {
                        Slot = player.inventorySlot,

                        WindowId = 0,

                        SlotData = player.GetHeldItem(),

                        StateId = player.Inventory.StateId++
                    });

                    break;
                }
            case DiggingStatus.StartedDigging:
                {
                    BroadcastPacket(new AcknowledgeBlockChangePacket
                    {
                        SequenceID = 0
                    });

                    if (player.Gamemode == Gamemode.Creative)
                    {
                        await player.world.SetBlockAsync(digging.Position, BlocksRegistry.Get(Material.Air));
                    }
                }
                break;
            case DiggingStatus.CancelledDigging:
                break;
            case DiggingStatus.FinishedDigging:
                {
                    BroadcastPacket(new AcknowledgeBlockChangePacket//TODO properly implement this
                    {
                        SequenceID = 0
                    });

                    BroadcastPacket(new SetBlockDestroyStagePacket
                    {
                        EntityId = player,
                        Position = digging.Position,
                        DestroyStage = -1
                    });

                    BroadcastPacket(new BlockUpdatePacket(digging.Position, 0));

                    var droppedItem = ItemsRegistry.Get(block.Material);

                    if (droppedItem.Id == 0) { break; }

                    var item = new ItemEntity
                    {
                        EntityId = player + player.world.GetTotalLoadedEntities() + 1,
                        Count = 1,
                        Id = droppedItem.Id,
                        Glowing = true,
                        World = player.world,
                        Position = digging.Position,
                        Server = this
                    };

                    TryAddEntity(player.world, item);

                    BroadcastPacket(new SpawnEntityPacket
                    {
                        EntityId = item.EntityId,
                        Uuid = item.Uuid,
                        Type = EntityType.Item,
                        Position = item.Position,
                        Pitch = 0,
                        Yaw = 0,
                        Data = 1,
                        Velocity = Velocity.FromVector(digging.Position + new VectorF(
                            (Globals.Random.NextFloat() * 0.5f) + 0.25f,
                            (Globals.Random.NextFloat() * 0.5f) + 0.25f,
                            (Globals.Random.NextFloat() * 0.5f) + 0.25f))
                    });

                    BroadcastPacket(new SetEntityMetadataPacket
                    {
                        EntityId = item.EntityId,
                        Entity = item
                    });
                    break;
                }
        }
    }

    public void Stop()
    {
        _cancelTokenSource.Cancel();
        _tcpListener.Stop();
        WorldGenerators.Clear();
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
                await UserCache.SaveAsync();
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
                await Events.InvokeServerTickAsync();

                keepAliveTicks++;
                if (keepAliveTicks > (Config.KeepAliveInterval / 50)) // to clarify: one tick is 50 milliseconds. 50 * 200 = 10000 millis means 10 seconds
                {
                    var keepAliveTime = DateTimeOffset.Now;

                    foreach (var client in _clients.Where(x => x.State == ClientState.Play))
                        client.SendKeepAlive(keepAliveTime);

                    keepAliveTicks = 0;
                }

                if (Config.Baah.HasValue)
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

    /// <summary>
    /// Registers the "obsidian-vanilla" entities and objects.
    /// </summary>
    /// Might be used for more stuff later so I'll leave this here - tides
    private void RegisterDefaults()
    {
        this.RegisterWorldGenerator<SuperflatGenerator>();
        this.RegisterWorldGenerator<OverworldGenerator>();
        this.RegisterWorldGenerator<IslandGenerator>();
        this.RegisterWorldGenerator<EmptyWorldGenerator>();
    }

    internal void UpdateStatusConsole()
    {
        var status = $"    tps:{Tps} c:{WorldManager.GeneratingChunkCount}/{WorldManager.LoadedChunkCount} r:{WorldManager.RegionCount}";
        ConsoleIO.UpdateStatusLine(status);
    }
}
