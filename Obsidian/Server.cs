using Microsoft.Extensions.Logging;
using Obsidian.API.Boss;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net.Rcon;
using Obsidian.Plugins;
using Obsidian.Utilities.Debugging;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Obsidian;

public partial class Server : IServer
{
    public static readonly ProtocolVersion DefaultProtocol = ProtocolVersion.v1_19;
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

    public Config Config { get; }
    public IConfig Configuration => Config;

    public ILogger Logger { get; }
    public LoggerProvider LoggerProvider { get; }

    public string Version { get; }
    public string ServerFolderPath { get; }
    public string PersistentDataPath { get; }
    public string Brand { get; } = "obsidian";

    public int Port { get; }

    public WorldManager WorldManager { get; private set; }
    public IWorld DefaultWorld => WorldManager.DefaultWorld;
    public IEnumerable<IPlayer> Players => GetPlayers();

    private readonly ConcurrentQueue<IClientboundPacket> chatMessagesQueue = new();
    private readonly ConcurrentHashSet<Client> clients = new();
    private readonly TcpListener tcpListener;

    private RconServer rconServer;

    internal string PermissionPath => Path.Combine(ServerFolderPath, "permissions");

    internal readonly CancellationTokenSource cts = new();

    /// <summary>
    /// Creates a new instance of <see cref="Server"/>.
    /// </summary>
    /// <param name="version">Version the server is running. <i>(unrelated to minecraft version)</i></param>
    public Server(Config config, string version, string path, List<ServerWorld> serverWorlds)
    {
        Config = config;

        Port = config.Port;
        Version = version;
        ServerFolderPath = path;

        tcpListener = new TcpListener(IPAddress.Any, Port);

        Operators = new OperatorList(this);

        LoggerProvider = new LoggerProvider(config.LogLevel);
        Logger = LoggerProvider.CreateLogger($"Server");
        // This stuff down here needs to be looked into
        Globals.PacketLogger = this.LoggerProvider.CreateLogger("Packets");
        PacketDebug.Logger = this.LoggerProvider.CreateLogger("PacketDebug");
        Registry.Logger = this.LoggerProvider.CreateLogger("Registry");

        Logger.LogDebug("Initializing command handler...");
        CommandsHandler = new CommandHandler(CommandHandler.DefaultPrefix);
        PluginManager = new PluginManager(Events, this, LoggerProvider.CreateLogger("Plugin Manager"), CommandsHandler);
        CommandsHandler.LinkPluginManager(PluginManager);

        Logger.LogDebug("Registering commands...");
        CommandsHandler.RegisterCommandClass(null, new MainCommandModule());

        Logger.LogDebug("Registering custom argument parsers...");
        CommandsHandler.AddArgumentParser(new LocationTypeParser());
        CommandsHandler.AddArgumentParser(new PlayerTypeParser());

        Logger.LogDebug("Registering command context type...");
        Logger.LogDebug("Done registering commands.");

        WorldManager = new WorldManager(this, Logger, serverWorlds);

        Events.PlayerLeave += OnPlayerLeave;
        Events.PlayerJoin += OnPlayerJoin;
        Events.PlayerAttackEntity += PlayerAttack;
        Events.PlayerInteract += OnPlayerInteract;

        PersistentDataPath = Path.Combine(ServerFolderPath, "persistentdata");

        Directory.CreateDirectory(PermissionPath);
        Directory.CreateDirectory(PersistentDataPath);

        if (Config.UDPBroadcast)
        {
            _ = Task.Run(async () =>
            {
                var udpClient = new UdpClient("224.0.2.60", 4445);
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(1500, cts.Token); // TODO (.NET 6), use PeriodicTimer
                    byte[] motd = Encoding.UTF8.GetBytes($"[MOTD]{config.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{config.Port}[/AD]");
                    await udpClient.SendAsync(motd, motd.Length);
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
            Registry.Recipes.Add(recipe.Name.ToSnakeCase(), recipe);
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
        chatMessagesQueue.Enqueue(new SystemChatMessagePacket(message, MessageType.System));
        Logger.LogInformation(message.Text);
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(PlayerChatMessagePacket message)
    {
        chatMessagesQueue.Enqueue(message);
        Logger.LogInformation(message.SignedMessage.Text);
    }

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(string message)
    {
        var chatMessage = ChatMessage.Simple(string.Empty)
            .AddExtra(message);

        chatMessagesQueue.Enqueue(new SystemChatMessagePacket(chatMessage, MessageType.System));
        Logger.LogInformation(message);
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
            this.Logger.LogDebug($"Registered {gen.Id}...");
    }

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        StartTime = DateTimeOffset.Now;

        Logger.LogInformation($"Launching Obsidian Server v{Version}");
        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Config.MulitplayerDebugMode && Config.OnlineMode)
        {
            Logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
            Stop();
            return;
        }

        await Task.WhenAll(Registry.RegisterCodecsAsync(),
                           Registry.RegisterRecipesAsync());

        Block.blockNames = BlocksRegistry.Names;
        Block.numericToBase = BlocksRegistry.NumericToBase;
        Block.stateToNumeric = BlocksRegistry.StateToNumeric;
        Block.stateToBase = BlocksRegistry.StateToBase;
        Block.Initialize();

        Logger.LogInformation($"Loading properties...");

        await (Operators as OperatorList).InitializeAsync();
        RegisterDefaults();

        ScoreboardManager = new ScoreboardManager(this);
        Logger.LogInformation("Loading plugins...");

        Directory.CreateDirectory(Path.Join(ServerFolderPath, "Plugins"));

        PluginManager.DirectoryWatcher.Filters = new[] { ".cs", ".dll" };
        PluginManager.DirectoryWatcher.Watch(Path.Join(ServerFolderPath, "Plugins"));

        await Task.WhenAll(Config.DownloadPlugins.Select(path => PluginManager.LoadPluginAsync(path)));

        await WorldManager.LoadWorldsAsync();

        if (!Config.OnlineMode)
            Logger.LogInformation($"Starting in offline mode...");

        Registry.RegisterCommands(this);

        if (Configuration.EnableRcon)
        {
            rconServer = new RconServer(LoggerProvider.CreateLogger("RCON"), Config, this, CommandsHandler);
        }

        loadTimeStopwatch.Stop();
        Logger.LogInformation($"Server loaded in {loadTimeStopwatch.Elapsed}");

        Logger.LogInformation($"Listening for new clients...");
        try
        {
            await Task.WhenAll(AcceptClientsAsync(cts.Token), LoopAsync(), ServerSaveAsync(), rconServer?.RunAsync(cts.Token) ?? Task.CompletedTask);
        }
        catch (TaskCanceledException)
        {
        }

        Logger.LogDebug("Flushing regions");
        await WorldManager.FlushLoadedWorldsAsync();

        Logger.LogWarning("Server is shutting down...");
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        tcpListener.Start();

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await tcpListener.AcceptSocketAsync(cancellationToken);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Listening for clients encountered an exception");
                break;
            }

            Logger.LogDebug($"New connection from client with IP {socket.RemoteEndPoint}");

            string ip = ((IPEndPoint)socket.RemoteEndPoint!).Address.ToString();
            if (Config.IpWhitelistEnabled && !Config.WhitelistedIPs.Contains(ip))
            {
                Logger.LogInformation($"{ip} is not whitelisted. Closing connection");
                socket.Disconnect(false);
                return;
            }

            // TODO Entity ids need to be unique on the entire server, not per world
            var client = new Client(socket, Config, Math.Max(0, clients.Count + WorldManager.DefaultWorld.GetTotalLoadedEntities()), this);

            clients.Add(client);

            client.Disconnected += client =>
            {
                clients.TryRemove(client);
                if (client.Player is not null)
                {
                    OnlinePlayers.Remove(client.Player.Uuid, out _);
                }
            };

            _ = Task.Run(client.StartConnectionAsync);
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
        var context = new CommandContext(CommandsHandler._prefix + input, new CommandSender(CommandIssuers.Console, null, Logger), null, this);
        try
        {
            await CommandsHandler.ProcessCommand(context);
        }
        catch (Exception e)
        {
            Logger.LogError(e, e.Message);
        }
    }

    internal IEnumerable<Player> PlayersInRange(World world, Vector worldPosition) => world.Players.Select(entry => entry.Value).Where(player => player.client.LoadedChunks.Contains(worldPosition.ToChunkCoord()));

    internal void BroadcastBlockChange(World world, Block block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.StateId);
        foreach (Player player in PlayersInRange(world, location))
        {
            player.client.SendPacket(packet);
        }
    }

    internal void BroadcastBlockChange(World world, Player initiator, Block block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.StateId);
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
        if (chat.Cancel)
            return;

        var playerChatMessagePacket = new PlayerChatMessagePacket(message, type, source.Player.Uuid)
        {
            UnsignedChatMessage = message,
            SenderDisplayName = source.Player.CustomName ?? source.Player.Username,
            MessageSignature = packet.Signature,
            Salt = packet.Salt,
            Timestamp = packet.Timestamp
        };
        BroadcastMessage(playerChatMessagePacket);
    }

    //internal async Task HandleIncomingMessageAsync(string message, string format, Client source, MessageType type = MessageType.Chat)
    //{
    //    format ??= "<{0}> {1}";

    //    if (message.StartsWith(CommandHandler.DefaultPrefix))
    //    {
    //        // TODO command logging
    //        // TODO error handling for commands
    //        var context = new CommandContext(message, new CommandSender(CommandIssuers.Client, source.Player, Logger), source.Player, this);
    //        try
    //        {
    //            await CommandsHandler.ProcessCommand(context);
    //        }
    //        catch (Exception e)
    //        {
    //            Logger.LogError(e, e.Message);
    //        }
    //    }
    //    else
    //    {
    //        var chat = await Events.InvokeIncomingChatMessageAsync(new IncomingChatMessageEventArgs(source.Player, message, format));

    //        if (!chat.Cancel)
    //            BroadcastMessage(string.Format(format, source.Player.Username, message), type);

    //        return;
    //    }
    //}

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
        Registry.RegisterCommands(this);
        foreach (Player player in Players)
            await player.client.SendCommandsAsync();
    }

    internal async Task DisconnectIfConnectedAsync(string username, ChatMessage reason = null)
    {
        var player = Players.FirstOrDefault(x => x.Username == username);
        if (player != null)
        {
            reason ??= "Connected from another location";

            await player.KickAsync(reason);
        }
    }

    private bool TryAddEntity(World world, Entity entity) => world.TryAddEntity(entity);

    internal async Task BroadcastPlayerDigAsync(PlayerDiggingStore store, Block block)
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
                        EntityId = player + player.World.GetTotalLoadedEntities() + 1,
                        Count = 1,
                        Id = droppedItem.AsItem().Id,
                        Glowing = true,
                        World = player.World,
                        Position = loc
                    };

                    TryAddEntity(player.World, item);

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
                        await player.World.SetBlockAsync(digging.Position, Block.Air);
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

                    var droppedItem = Registry.GetItem(block.Material);

                    if (droppedItem.Id == 0) { break; }

                    var item = new ItemEntity
                    {
                        EntityId = player + player.World.GetTotalLoadedEntities() + 1,
                        Count = 1,
                        Id = droppedItem.Id,
                        Glowing = true,
                        World = player.World,
                        Position = digging.Position,
                        Server = this
                    };

                    TryAddEntity(player.World, item);

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
        cts.Cancel();
        tcpListener.Stop();
        WorldGenerators.Clear();
        foreach (var client in clients)
        {
            client.Disconnect();
            client.Dispose();
        }
    }

    private async Task ServerSaveAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cts.Token);
            await WorldManager.FlushLoadedWorldsAsync();
        }
    }

    private async Task LoopAsync()
    {
        var keepAliveTicks = 0;

        var tpsMeasure = new TpsMeasure();
        var stopwatch = Stopwatch.StartNew();
        var timer = new BalancingTimer(50, cts.Token);
        while (await timer.WaitForNextTickAsync())
        {
            await Events.InvokeServerTickAsync();

            keepAliveTicks++;
            if (keepAliveTicks > 50)
            {
                var keepAliveId = DateTime.Now.Millisecond;

                foreach (var client in clients.Where(x => x.State == ClientState.Play))
                    client.ProcessKeepAlive(keepAliveId);

                keepAliveTicks = 0;
            }

            if (Config.Baah.HasValue)
            {
                foreach (Player player in Players)
                {
                    var soundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z);
                    await player.SendSoundAsync(Sounds.EntitySheepAmbient, soundPosition, SoundCategory.Master, 1.0f, 1.0f);
                }
            }

            while (chatMessagesQueue.TryDequeue(out IClientboundPacket packet))
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
        this.RegisterWorldGenerator<EmptyWorldGenerator>();
    }

    internal void UpdateStatusConsole()
    {
        var status = $"    tps:{Tps} c:{WorldManager.GeneratingChunkCount}/{WorldManager.LoadedChunkCount} r:{WorldManager.RegionCount}";
        ConsoleIO.UpdateStatusLine(status);
    }
}
