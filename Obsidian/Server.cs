using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Exceptions;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Plugins;
using Obsidian.Util;
using Obsidian.Util.Debug;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry;
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
            
            this.LoggerProvider = new LoggerProvider(Globals.Config.LogLevel);
            this.Logger = this.LoggerProvider.CreateLogger($"Server/{this.Id}");
            // This stuff down here needs to be looked into
            Globals.PacketLogger = this.LoggerProvider.CreateLogger("Packets");
            PacketDebug.Logger = this.LoggerProvider.CreateLogger("PacketDebug");
            //Registry.Logger = this.LoggerProvider.CreateLogger("Registry");
            
            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;
            this.ServerFolderPath = Path.GetFullPath($"Server-{this.Id}");

            this.tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.clients = new ConcurrentHashSet<Client>();

            this.cts = new CancellationTokenSource();

            this.chatMessages = new ConcurrentQueue<QueueChat>();
            this.placed = new ConcurrentQueue<PlayerBlockPlacement>();

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

            this.Events = new MinecraftEventHandler();

            this.Operators = new OperatorList(this);

            this.Events.PlayerLeave += this.OnPlayerLeave;
            this.Events.PlayerJoin += this.OnPlayerJoin;
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

        /// <summary>
        /// Sends a message to all players on the server.
        /// </summary>
        public Task BroadcastAsync(IChatMessage message, MessageType type = MessageType.Chat)
        {
            this.chatMessages.Enqueue(new QueueChat() { Message = message, Type = type });
            this.Logger.LogInformation(message.Text);

            return Task.CompletedTask;
        }

        public Task BroadcastAsync(string message, MessageType type = MessageType.Chat)
        {
            this.chatMessages.Enqueue(new QueueChat() { Message = IChatMessage.Simple(message), Type = type });
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
                               Registry.RegisterBiomesAsync(),
                               Registry.RegisterDimensionsAsync(),
                               Registry.RegisterTagsAsync(),
                               Registry.RegisterRecipesAsync());

            Block.Initialize();
            Entity.Initialize();
            Cube.Initialize();
            ServerImplementationRegistry.RegisterServerImplementations();

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
            if (!this.World.Load())
            {
                if (!this.WorldGenerators.TryGetValue(this.Config.Generator, out WorldGenerator value))
                    this.Logger.LogWarning($"Unknown generator type {this.Config.Generator}");
                var gen = value ?? new SuperflatGenerator();
                this.Logger.LogInformation($"Creating new {gen.Id} ({gen}) world...");
                this.World.Init(gen);
                this.World.Save();
            }

            if (!this.Config.OnlineMode)
                this.Logger.LogInformation($"Starting in offline mode...");

            Registry.RegisterCommands(this);

            _ = Task.Run(this.ServerLoop);

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

        internal async Task BroadcastBlockPlacementAsync(Player player, Block block, Position location)
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
            var context = new CommandContext(message, source.Player, this);
            try
            {
                await Commands.ProcessCommand(context);
            }
            catch (CommandArgumentParsingException)
            {
                await source.Player.SendMessageAsync(new ChatMessage() { Text = $"{ChatColor.Red}Invalid arguments! Parsing failed." });
            }
            catch (CommandExecutionCheckException)
            {
                await source.Player.SendMessageAsync(new ChatMessage() { Text = $"{ChatColor.Red}You can not execute this command." });
            }
            catch (CommandNotFoundException)
            {
                await source.Player.SendMessageAsync(new ChatMessage() { Text = $"{ChatColor.Red}No such command was found." });
            }
            catch (NoSuchParserException)
            {
                await source.Player.SendMessageAsync(new ChatMessage() { Text = $"{ChatColor.Red}The command you executed has a argument that has no matching parser." });
            }
            catch (InvalidCommandOverloadException)
            {
                await source.Player.SendMessageAsync(new ChatMessage() { Text = $"{ChatColor.Red}No such overload is available for this command." });
            }
            catch (Exception e)
            {
                await source.Player.SendMessageAsync(new ChatMessage() { Text = $"{ChatColor.Red}Critically failed executing command: {e.Message}" });
                Logger.LogError(e, e.Message);
            }
        }

        internal async Task BroadcastPacketAsync(IPacket packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.Id)))
                await player.client.QueuePacketAsync(packet);
        }

        internal void BroadcastPacketWithoutQueue(IPacket packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.Id)))
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

        private bool TryAddEntity(World world, Entity entity)
        {
            this.Logger.LogDebug($"{entity.Id} new ID");

            return world.TryAddEntity(entity);
        }

        internal void BroadcastPlayerDig(PlayerDiggingStore store)
        {
            var digging = store.Packet;

            var block = this.World.GetBlock(digging.Position);

            var player = this.OnlinePlayers.GetValueOrDefault(store.Player);

            switch (digging.Status)
            {
                case DiggingStatus.DropItem:
                    {
                        var droppedItem = player.GetHeldItem();

                        if (droppedItem is null || droppedItem.Type == Material.Air)
                            return;

                        var loc = new PositionF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

                        var item = new ItemEntity
                        {
                            EntityId = player + this.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = droppedItem.GetItem().Id,
                            EntityBitMask = EntityBitMask.Glowing,
                            World = this.World,
                            Position = loc
                        };

                        this.TryAddEntity(player.World, item);

                        var f8 = Math.Sin(player.Pitch.Degrees * ((float)Math.PI / 180f));
                        var f2 = Math.Cos(player.Pitch.Degrees * ((float)Math.PI / 180f));

                        var f3 = Math.Sin(player.Yaw.Degrees * ((float)Math.PI / 180f));
                        var f4 = Math.Cos(player.Yaw.Degrees * ((float)Math.PI / 180f));

                        var f5 = Globals.Random.NextDouble() * ((float)Math.PI * 2f);
                        var f6 = 0.02f * Globals.Random.NextDouble();

                        var vel = new Velocity((short)((double)(-f3 * f2 * 0.3F) + Math.Cos((double)f5) * (double)f6),
                            (short)((double)(-f8 * 0.3F + 0.1F + (Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.1F)),
                            (short)((double)(f4 * f2 * 0.3F) + Math.Sin((double)f5) * (double)f6));

                        this.BroadcastPacketWithoutQueue(new SpawnEntity
                        {
                            EntityId = item.EntityId,
                            Uuid = Guid.NewGuid(),
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
                            Slot = player.CurrentSlot,

                            WindowId = 0,

                            SlotData = player.Inventory.GetItem(player.CurrentSlot) - 1
                        });

                        player.Inventory.RemoveItem(player.CurrentSlot);
                        break;
                    }
                case DiggingStatus.StartedDigging:
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
                    break;
                case DiggingStatus.CancelledDigging:
                    this.BroadcastPacketWithoutQueue(new AcknowledgePlayerDigging
                    {
                        Position = digging.Position,
                        Block = block.Id,
                        Status = digging.Status,
                        Successful = true
                    });
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

                        var itemId = Registry.GetItem(block.Material).Id;

                        if (itemId == 0) { break; }

                        var item = new ItemEntity
                        {
                            EntityId = player + this.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = itemId,
                            EntityBitMask = EntityBitMask.Glowing,
                            World = this.World,
                            Position = digging.Position + new PositionF(
                                (Globals.Random.NextSingle() * 0.5f) + 0.25f,
                                (Globals.Random.NextSingle() * 0.5f) + 0.25f,
                                (Globals.Random.NextSingle() * 0.5f) + 0.25f)
                        };

                        this.TryAddEntity(player.World, item);

                        this.BroadcastPacketWithoutQueue(new SpawnEntity
                        {
                            EntityId = item.EntityId,
                            Uuid = Guid.NewGuid(),
                            Type = EntityType.Item,
                            Position = item.Position,
                            Pitch = 0,
                            Yaw = 0,
                            Data = 1,
                            Velocity = Velocity.FromPosition(digging.Position)
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

        private async Task ServerLoop()
        {
            var keepAliveTicks = 0;

            short itersPerSecond = 0;
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

                // if Stopwatch elapsed time more than 1000 ms, reset counter, restart stopwatch, and set TPS property
                itersPerSecond++;
                if (stopWatch.ElapsedMilliseconds >= 1000L)
                {
                    TPS = itersPerSecond;
                    itersPerSecond = 0;
                    stopWatch.Restart();
                }
                _ = Task.Run(() => World.ManageChunks());
            }
        }

        /// <summary>
        /// Registers the "obsidian-vanilla" entities and objects.
        /// </summary>
        private async Task RegisterDefaultAsync()
        {
            await this.RegisterAsync(new SuperflatGenerator());
            await this.RegisterAsync(new OverworldGenerator(Config.Seed));
            await this.RegisterAsync(new OverworldDebugGenerator(Config.Seed));
        }

        private async Task SendSpawnPlayerAsync(IPlayer joined)
        {
            foreach (var (_, player) in this.OnlinePlayers.Except(joined.Uuid))
            {
                var joinedPlayer = joined as Player;
                //await player.client.QueuePacketAsync(new EntityMovement { EntityId = joined.EntityId });
                await player.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = joinedPlayer.Id,
                    Uuid = joinedPlayer.Uuid,
                    Position = joinedPlayer.Position,
                    Yaw = 0,
                    Pitch = 0
                });

                //await joined.client.QueuePacketAsync(new EntityMovement { EntityId = player.EntityId });
                await joinedPlayer.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = player.Id,
                    Uuid = player.Uuid,
                    Position = player.Position,
                    Yaw = 0,
                    Pitch = 0
                });
            }
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
        private async Task OnPlayerLeave(PlayerLeaveEventArgs e)
        {
            foreach (var (_, other) in this.OnlinePlayers.Except(e.Player.Uuid))
                await other.client.RemovePlayerFromListAsync(e.Player);

            await this.BroadcastAsync(string.Format(this.Config.LeaveMessage, e.Player.Username));
        }

        private async Task OnPlayerJoin(PlayerJoinEventArgs e)
        {
            var joined = e.Player;
            await this.BroadcastAsync(string.Format(this.Config.JoinMessage, e.Player.Username));
            foreach (var (_, other) in this.OnlinePlayers)
                await other.client.AddPlayerToListAsync(joined);

            // Need a delay here, otherwise players start flying
            await Task.Delay(500);
            await this.SendSpawnPlayerAsync(joined);
        }
        #endregion Events

        private struct QueueChat
        {
            public IChatMessage Message;
            public MessageType Type;
        }
    }
}
