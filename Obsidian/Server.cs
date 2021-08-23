using Microsoft.Extensions.Logging;
using Obsidian.API;
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
using Obsidian.Plugins;
using Obsidian.Utilities;
using Obsidian.Utilities.Debug;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian
{
    public class Server : IServer
    {
        private readonly ConcurrentQueue<QueueChat> chatMessages;
        private readonly ConcurrentQueue<PlayerBlockPlacement> placed;
        private readonly ConcurrentHashSet<Client> clients;
        private readonly TcpListener tcpListener;
        private readonly UdpClient udpClient;

        internal string PermissionPath => Path.Combine(this.ServerFolderPath, "permissions");

        internal readonly CancellationTokenSource cts;

        internal static byte LastInventoryId;

        public const ProtocolVersion protocol = ProtocolVersion.v1_16_5;
        public ProtocolVersion Protocol => protocol;

        public short TPS { get; private set; }
        public DateTimeOffset StartTime { get; private set; }

        public MinecraftEventHandler Events { get; }
        public PluginManager PluginManager { get; }

        public IOperatorList Operators { get; }

        public IScoreboardManager ScoreboardManager { get; private set; }

        internal ConcurrentDictionary<Guid, Inventory> CachedWindows { get; } = new ConcurrentDictionary<Guid, Inventory>();

        public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new ConcurrentDictionary<Guid, Player>();

        public ConcurrentDictionary<string, World> Worlds { get; private set; } = new ConcurrentDictionary<string, World>();

        public Dictionary<string, WorldGenerator> WorldGenerators { get; } = new Dictionary<string, WorldGenerator>();

        public HashSet<string> RegisteredChannels { get; private set; } = new HashSet<string>();

        public CommandHandler Commands { get; }
        public Config Config { get; }
        public IConfig Configuration => Config;

        public ILogger Logger { get; }

        public LoggerProvider LoggerProvider { get; }

        public int TotalTicks { get; private set; }

        public int Id { get; }
        public string Version { get; }
        public int Port { get; }

        public World World { get; private set; }
        public IWorld DefaultWorld => World;

        public string ServerFolderPath { get; }

        /// <summary>
        /// Creates a new instance of <see cref="Server"/>.
        /// </summary>
        /// <param name="version">Version the server is running. <i>(unrelated to minecraft version)</i></param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;
            this.ServerFolderPath = Path.GetFullPath($"Server-{this.Id}");

            this.tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.clients = new ConcurrentHashSet<Client>();

            this.cts = new CancellationTokenSource();

            this.chatMessages = new ConcurrentQueue<QueueChat>();
            this.placed = new ConcurrentQueue<PlayerBlockPlacement>();

            this.Events = new MinecraftEventHandler();

            this.Operators = new OperatorList(this);

            this.LoggerProvider = new LoggerProvider(Globals.Config.LogLevel);
            this.Logger = this.LoggerProvider.CreateLogger($"Server/{this.Id}");
            // This stuff down here needs to be looked into
            Globals.PacketLogger = this.LoggerProvider.CreateLogger("Packets");
            PacketDebug.Logger = this.LoggerProvider.CreateLogger("PacketDebug");
            //Registry.Logger = this.LoggerProvider.CreateLogger("Registry");

            Logger.LogDebug("Initializing command handler...");
            this.Commands = new CommandHandler("/");
            this.PluginManager = new PluginManager(Events, this, LoggerProvider.CreateLogger("Plugin Manager"), this.Commands);
            this.Commands.LinkPluginManager(this.PluginManager);

            Logger.LogDebug("Registering commands...");
            this.Commands.RegisterCommandClass(null, new MainCommandModule());

            Logger.LogDebug("Registering custom argument parsers...");
            this.Commands.AddArgumentParser(new LocationTypeParser());
            this.Commands.AddArgumentParser(new PlayerTypeParser());

            Logger.LogDebug("Registering command context type...");
            Logger.LogDebug("Done registering commands.");

            this.Events.PlayerLeave += this.OnPlayerLeave;
            this.Events.PlayerJoin += this.OnPlayerJoin;
            this.Events.PlayerAttackEntity += this.PlayerAttack;

            if (!Directory.Exists(this.PermissionPath))
                Directory.CreateDirectory(this.PermissionPath);

            if (this.Config.UDPBroadcast)
            {
                this.udpClient = new UdpClient("224.0.2.60", 4445);
                _ = Task.Run(async () =>
                {
                    while (!this.cts.IsCancellationRequested)
                    {
                        await Task.Delay(1500); // Official clients do this too.
                        var str = Encoding.UTF8.GetBytes($"[MOTD]{config.Motd.Replace('[', '(').Replace(']', ')')}[/MOTD][AD]{config.Port}[/AD]");
                        await this.udpClient.SendAsync(str, str.Length);
                    }
                });
            }

        }

        public void RegisterCommandClass<T>(PluginContainer plugin, T instance) =>
            this.Commands.RegisterCommandClass<T>(plugin, instance);

        public void RegisterArgumentHandler<T>(T parser) where T : BaseArgumentParser =>
            this.Commands.AddArgumentParser(parser);

        //TODO make sure to re-send recipes
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
        public bool IsPlayerOnline(string username) => this.OnlinePlayers.Any(x => x.Value.Username == username);

        public bool IsPlayerOnline(Guid uuid) => this.OnlinePlayers.ContainsKey(uuid);

        public IPlayer GetPlayer(string username) => this.OnlinePlayers.FirstOrDefault(player => player.Value.Username == username).Value;

        public IPlayer GetPlayer(Guid uuid) => this.OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

        public IPlayer GetPlayer(int entityId) => this.OnlinePlayers.FirstOrDefault(player => player.Value.EntityId == entityId).Value;

        /// <summary>
        /// Sends a message to all players on the server.
        /// </summary>
        public Task BroadcastAsync(ChatMessage message, MessageType type = MessageType.Chat)
        {
            this.chatMessages.Enqueue(new QueueChat() { Message = message, Type = type });
            this.Logger.LogInformation(message.Text);

            return Task.CompletedTask;
        }

        public Task BroadcastAsync(string message, MessageType type = MessageType.Chat)
        {
            this.chatMessages.Enqueue(new QueueChat() { Message = ChatMessage.Simple(message), Type = type });
            this.Logger.LogInformation(message);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers a new entity to the server.
        /// </summary>
        /// <param name="input">A compatible entry.</param>
        /// <exception cref="Exception">Thrown if unknown/unhandable type has been passed.</exception>
        public Task RegisterAsync(params object[] input)
        {
            foreach (object item in input)
            {
                switch (item)
                {
                    case WorldGenerator generator:
                        Logger.LogDebug($"Registering {generator.Id}...");
                        this.WorldGenerators.Add(generator.Id, generator);
                        break;

                    default:
                        throw new Exception($"Input ({item.GetType().Name}) can't be handled by RegisterAsync.");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts this server asynchronously.
        /// </summary>
        public async Task StartServerAsync()
        {
            this.StartTime = DateTimeOffset.Now;

            this.Logger.LogInformation($"Launching Obsidian Server v{Version} with ID {Id}");
            var stopwatch = Stopwatch.StartNew();

            // Check if MPDM and OM are enabled, if so, we can't handle connections
            if (this.Config.MulitplayerDebugMode && this.Config.OnlineMode)
            {
                this.Logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                this.StopServer();
                return;
            }

            await Task.WhenAll(Registry.RegisterBlocksAsync(),
                               Registry.RegisterItemsAsync(),
                               Registry.RegisterCodecsAsync(),
                               Registry.RegisterTagsAsync(),
                               Registry.RegisterRecipesAsync());

            Block.Initialize();
            //ServerImplementationRegistry.RegisterServerImplementations();

            this.Logger.LogInformation($"Loading properties...");

            await (this.Operators as OperatorList).InitializeAsync();
            await this.RegisterDefaultAsync();

            this.ScoreboardManager = new ScoreboardManager(this);
            this.Logger.LogInformation("Loading plugins...");

            Directory.CreateDirectory(Path.Join(ServerFolderPath, "plugins")); // Creates if doesn't exist.

            this.PluginManager.DirectoryWatcher.Filters = new[] { ".cs", ".dll" };
            this.PluginManager.DefaultPermissions = API.Plugins.PluginPermissions.All;
            this.PluginManager.DirectoryWatcher.Watch(Path.Join(ServerFolderPath, "plugins"));

            await Task.WhenAll(Config.DownloadPlugins.Select(path => PluginManager.LoadPluginAsync(path)));

            this.World = new World("world1", this);
            if (!await this.World.LoadAsync())
            {
                if (!this.WorldGenerators.TryGetValue(this.Config.Generator, out WorldGenerator value))
                    this.Logger.LogWarning($"Unknown generator type {this.Config.Generator}");

                var gen = value ?? new SuperflatGenerator();
                this.Logger.LogInformation($"Creating new {gen.Id} ({gen}) world...");
                await World.Init(gen);
                this.World.Save();
            }

            if (!this.Config.OnlineMode)
                this.Logger.LogInformation($"Starting in offline mode...");

            Registry.RegisterCommands(this);

            _ = Task.Run(this.ServerLoop);

            _ = Task.Run(this.ServerSaveAsync);

            this.Logger.LogInformation($"Listening for new clients...");

            stopwatch.Stop();

            Logger.LogInformation($"Server-{Id} loaded in {stopwatch.Elapsed}");

            this.tcpListener.Start();

            while (!this.cts.IsCancellationRequested)
            {
                var tcp = await this.tcpListener.AcceptTcpClientAsync();
                this.Logger.LogDebug($"New connection from client with IP {tcp.Client.RemoteEndPoint}");

                var client = new Client(tcp, this.Config, Math.Max(0, this.clients.Count + this.World.TotalLoadedEntities()), this);
                this.clients.Add(client);

                client.Disconnected += client => clients.TryRemove(client);

                _ = Task.Run(client.StartConnectionAsync);
            }

            this.Logger.LogWarning("Server is shutting down...");
        }

        internal async Task ExecuteCommand(string input)
        {
            var context = new CommandContext(Commands._prefix + input, new CommandSender(CommandIssuers.Console, null, Logger), null, this);
            try
            {
                await Commands.ProcessCommand(context);
            }
            catch (Exception e)
            {
                Logger.LogError(e, e.Message);
            }
        }

        internal async Task BroadcastBlockPlacementAsync(Player player, Block block, Vector location)
        {
            foreach (var (_, other) in this.OnlinePlayers.Except(player))
            {
                var client = other.client;

                await client.QueuePacketAsync(new BlockChange(location, block.Id));
            }
        }

        internal async Task ParseMessageAsync(string message, string format, Client source, MessageType type = MessageType.Chat)
        {
            if (format == null) format = "<{0}> {1}";
            if (!message.StartsWith('/'))
            {
                var chat = await this.Events.InvokeIncomingChatMessageAsync(new IncomingChatMessageEventArgs(source.Player, message, format));

                if (!chat.Cancel)
                    await this.BroadcastAsync(string.Format(format, source.Player.Username, message), type);

                return;
            }

            // TODO command logging
            // TODO error handling for commands
            var context = new CommandContext(message, new CommandSender(CommandIssuers.Client, source.Player, Logger), source.Player, this);
            try
            {
                await Commands.ProcessCommand(context);
            }
            catch (Exception e)
            {
                Logger.LogError(e, e.Message);
            }
        }

        internal async Task BroadcastPacketAsync(IClientboundPacket packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.EntityId)))
                await player.client.QueuePacketAsync(packet);
        }

        internal void BroadcastPacketWithoutQueue(IClientboundPacket packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.EntityId)))
                player.client.SendPacket(packet);
        }

        internal async Task BroadcastNewCommandsAsync()
        {
            Registry.RegisterCommands(this);
            foreach (var (_, player) in this.OnlinePlayers)
                await player.client.SendDeclareCommandsAsync();
        }

        internal async Task DisconnectIfConnectedAsync(string username, ChatMessage reason = null)
        {
            var player = this.OnlinePlayers.Values.FirstOrDefault(x => x.Username == username);
            if (player != null)
            {
                reason ??= "Connected from another location";

                await player.KickAsync(reason);
            }
        }

        private bool TryAddEntity(World world, Entity entity) => world.TryAddEntity(entity);

        internal void BroadcastPlayerDig(PlayerDiggingStore store)
        {
            var digging = store.Packet;

            var b = this.World.GetBlock(digging.Position);
            if (b is null) { return; }
            var block = (Block)b;

            var player = this.OnlinePlayers.GetValueOrDefault(store.Player);

            switch (digging.Status)
            {
                case DiggingStatus.DropItem:
                    {
                        var droppedItem = player.GetHeldItem();

                        if (droppedItem is null || droppedItem.Type == Material.Air)
                            return;

                        var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

                        var item = new ItemEntity
                        {
                            EntityId = player + this.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = droppedItem.GetItem().Id,
                            Glowing = true,
                            World = this.World,
                            Position = loc
                        };

                        this.TryAddEntity(player.World, item);

                        var lookDir = player.GetLookDirection();

                        var vel = Velocity.FromDirection(loc, lookDir);//TODO properly shoot the item towards the direction the players looking at

                        this.BroadcastPacketWithoutQueue(new SpawnEntity
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
                        this.BroadcastPacketWithoutQueue(new EntityMetadata
                        {
                            EntityId = item.EntityId,
                            Entity = item
                        });

                        player.client.SendPacket(new SetSlot
                        {
                            Slot = player.inventorySlot,

                            WindowId = 0,

                            SlotData = player.Inventory.GetItem(player.inventorySlot) - 1
                        });

                        player.Inventory.RemoveItem(player.inventorySlot);
                        break;
                    }
                case DiggingStatus.StartedDigging:
                    {
                        this.BroadcastPacketWithoutQueue(new AcknowledgePlayerDigging
                        {
                            Position = digging.Position,
                            Block = block.Id,
                            Status = digging.Status,
                            Successful = true
                        });

                        if (player.Gamemode == Gamemode.Creative)
                        {
                            this.BroadcastPacketWithoutQueue(new BlockChange(digging.Position, 0));

                            this.World.SetBlock(digging.Position, Block.Air);
                        }
                    }
                    break;
                case DiggingStatus.CancelledDigging:
                    break;
                case DiggingStatus.FinishedDigging:
                    {
                        this.BroadcastPacketWithoutQueue(new AcknowledgePlayerDigging
                        {
                            Position = digging.Position,
                            Block = block.Id,
                            Status = digging.Status,
                            Successful = true
                        });

                        this.BroadcastPacketWithoutQueue(new BlockBreakAnimation
                        {
                            EntityId = player,
                            Position = digging.Position,
                            DestroyStage = -1
                        });

                        this.BroadcastPacketWithoutQueue(new BlockChange(digging.Position, 0));

                        this.World.SetBlock(digging.Position, Block.Air);

                        var droppedItem = Registry.GetItem(block.Material);

                        if (droppedItem.Id == 0) { break; }

                        var item = new ItemEntity
                        {
                            EntityId = player + this.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = droppedItem.Id,
                            Glowing = true,
                            World = this.World,
                            Position = digging.Position,
                            Server = this
                        };

                        this.TryAddEntity(player.World, item);

                        this.BroadcastPacketWithoutQueue(new SpawnEntity
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

                        this.BroadcastPacketWithoutQueue(new EntityMetadata
                        {
                            EntityId = item.EntityId,
                            Entity = item
                        });
                        break;
                    }
            }
        }

        internal void StopServer()
        {
            this.cts.Cancel();
            this.tcpListener.Stop();
            this.WorldGenerators.Clear();

            foreach (var client in this.clients)
                client.Disconnect();
        }

        private async Task ServerSaveAsync()
        {
            while (!this.cts.IsCancellationRequested)
            {
                await Task.Delay(1000 * 60 * 5); // 5 minutes
                await World.FlushRegionsAsync();
            }
        }

        private async Task ServerLoop()
        {
            var keepAliveTicks = 0;

            var stopWatch = Stopwatch.StartNew(); // for TPS measuring

            while (!this.cts.IsCancellationRequested)
            {
                await Task.Delay(50, cts.Token);

                await this.Events.InvokeServerTickAsync();

                keepAliveTicks++;
                if (keepAliveTicks > 50)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var client in this.clients.Where(x => x.State == ClientState.Play))
                        client.ProcessKeepAlive(keepaliveid);

                    keepAliveTicks = 0;
                }

                if (Config.Baah.HasValue)
                {
                    foreach (var (uuid, player) in this.OnlinePlayers)
                    {
                        var soundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z);
                        await player.SendSoundAsync(Sounds.EntitySheepAmbient, soundPosition, SoundCategory.Master, 1.0f, 1.0f);
                    }
                }

                while (chatMessages.TryDequeue(out QueueChat msg))
                {
                    foreach (var (uuid, player) in this.OnlinePlayers)
                    {
                        await player.SendMessageAsync(msg.Message, msg.Type);
                    }
                }

                TPS = (short)(1.0 / stopWatch.Elapsed.TotalSeconds);
                stopWatch.Restart();

                await World.ManageChunksAsync();
                UpdateStatusConsole();

            }
            await World.FlushRegionsAsync();
        }

        /// <summary>
        /// Registers the "obsidian-vanilla" entities and objects.
        /// </summary>
        private async Task RegisterDefaultAsync()
        {
            await this.RegisterAsync(new SuperflatGenerator());
            await this.RegisterAsync(new OverworldGenerator(Config.Seed));
        }

        public IEnumerable<IPlayer> Players => GetPlayers();
        private IEnumerable<IPlayer> GetPlayers()
        {
            foreach (var (_, player) in OnlinePlayers)
            {
                yield return player;
            }
        }

        #region Events
        private async Task PlayerAttack(PlayerAttackEntityEventArgs e)
        {
            var entity = e.Entity;
            var attacker = e.Attacker;

            if (entity is IPlayer player)
            {
                await player.DamageAsync(attacker);
            }
        }

        private async Task OnPlayerLeave(PlayerLeaveEventArgs e)
        {
            var player = e.Player as Player;

            await player.SaveAsync();

            this.World.RemovePlayer(player);

            var destroy = new DestroyEntities
            {
                EntityIds = new() { player.EntityId }
            };
            foreach (var (_, other) in this.OnlinePlayers.Except(player.Uuid))
            {
                await other.client.RemovePlayerFromListAsync(player);
                if (other.VisiblePlayers.Contains(player.EntityId))
                    await other.client.QueuePacketAsync(destroy);
            }

            await this.BroadcastAsync(string.Format(this.Config.LeaveMessage, e.Player.Username));
        }

        private async Task OnPlayerJoin(PlayerJoinEventArgs e)
        {
            var joined = e.Player as Player;

            this.World.AddPlayer(joined);//TODO Gotta make sure we add the player to whatever world they were last in so this has to change

            await this.BroadcastAsync(string.Format(this.Config.JoinMessage, e.Player.Username));
            foreach (var (_, other) in this.OnlinePlayers)
            {
                await other.client.AddPlayerToListAsync(joined);
            }
        }
        #endregion Events

        internal void UpdateStatusConsole()
        {
            var cl = 0;
            foreach (var r in World.Regions.Values) { cl += r.LoadedChunkCount; }
            var status = $"    tps:{TPS} c:{World.ChunksToGen.Count}/{cl} r:{World.RegionsToLoad.Count}/{World.Regions.Count}";
            ConsoleIO.UpdateStatusLine(status);
        }

        private struct QueueChat
        {
            public ChatMessage Message;
            public MessageType Type;
        }
    }
}
