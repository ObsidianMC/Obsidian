using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Blocks;
using Obsidian.Chat;
using Obsidian.CommandFramework;
using Obsidian.CommandFramework.ArgumentParsers;
using Obsidian.CommandFramework.Entities;
using Obsidian.CommandFramework.Exceptions;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Items;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Net.Packets.Play.Server;
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

        internal readonly CancellationTokenSource cts;
        private readonly TcpListener tcpListener;

        public const ProtocolVersion protocol = ProtocolVersion.v1_16_4;
        public ProtocolVersion Protocol => protocol;

        public short TPS { get; private set; }
        public DateTimeOffset StartTime { get; private set; }

        public MinecraftEventHandler Events { get; }
        public PluginManager PluginManager { get; }

        public IOperatorList Operators { get; }

        internal ConcurrentDictionary<int, Inventory> CachedWindows { get; } = new ConcurrentDictionary<int, Inventory>();

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

        public string ServerFolderPath => Path.GetFullPath($"Server-{this.Id}");

        /// <summary>
        /// Creates a new Server instance.
        /// </summary>
        /// <param name="version">Version the server is running. <i>(independent of minecraft version)</i></param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            ServerImplementationRegistry.RegisterServerImplementations();

            this.LoggerProvider = new LoggerProvider(Globals.Config.LogLevel);
            this.Logger = this.LoggerProvider.CreateLogger($"Server/{this.Id}");
            // This stuff down here needs to be looked into
            Globals.PacketLogger = this.LoggerProvider.CreateLogger("Packets");
            PacketDebug.Logger = this.LoggerProvider.CreateLogger("PacketDebug");
            Registry.Logger = this.LoggerProvider.CreateLogger("Registry");

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;

            this.tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.clients = new ConcurrentHashSet<Client>();

            this.cts = new CancellationTokenSource();

            this.chatMessages = new ConcurrentQueue<QueueChat>();
            this.placed = new ConcurrentQueue<PlayerBlockPlacement>();

            Logger.LogDebug("Initializing command handler...");
            this.Commands = new CommandHandler("/");

            Logger.LogDebug("Registering commands...");
            this.Commands.RegisterCommandClass<MainCommandModule>();

            Logger.LogDebug("Registering custom argument parsers...");
            this.Commands.AddArgumentParser(new LocationTypeParser());
            this.Commands.AddArgumentParser(new PlayerTypeParser());

            Logger.LogDebug("Registering command context type...");
            Logger.LogDebug("Done registering commands.");

            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(Events, this, LoggerProvider.CreateLogger("Plugin Manager"));

            this.Operators = new OperatorList(this);

            this.Events.PlayerLeave += this.OnPlayerLeave;
            this.Events.PlayerJoin += this.OnPlayerJoin;
            this.Events.ServerTick += this.OnServerTick;
        }

        
        public void RegisterCommandClass<T>() where T : BaseCommandClass => 
            this.Commands.RegisterCommandClass<T>();

        public void RegisterArgumentHandler<T>(T parser) where T : BaseArgumentParser => 
            this.Commands.AddArgumentParser(parser);

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
        /// Starts this server.
        /// </summary>
        public async Task StartServerAsync()
        {
            this.StartTime = DateTimeOffset.Now;

            this.Logger.LogInformation($"Launching Obsidian Server v{Version} with ID {Id}");

            // Check if MPDM and OM are enabled, if so, we can't handle connections
            if (this.Config.MulitplayerDebugMode && this.Config.OnlineMode)
            {
                this.Logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                this.StopServer();
                return;
            }

            await Registry.RegisterBlocksAsync();
            await Registry.RegisterItemsAsync();
            await Registry.RegisterBiomesAsync();
            await Registry.RegisterDimensionsAsync();
            await Registry.RegisterTagsAsync();

            PacketHandler.RegisterHandlers();

            this.Logger.LogInformation($"Loading properties...");
            await (this.Operators as OperatorList).InitializeAsync();
            await this.RegisterDefaultAsync();

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

            _ = Task.Run(this.ServerLoop);

            this.Logger.LogDebug($"Listening for new clients...");

            this.tcpListener.Start();

            while (!this.cts.IsCancellationRequested)
            {
                if (this.tcpListener.Pending())
                {
                    var tcp = await this.tcpListener.AcceptTcpClientAsync();
                    this.Logger.LogDebug($"New connection from client with IP {tcp.Client.RemoteEndPoint}");

                    var client = new Client(tcp, this.Config, Math.Max(0, this.clients.Count + this.World.TotalLoadedEntities()), this);
                    this.clients.Add(client);

                    _ = Task.Run(client.StartConnectionAsync);
                }
                await Task.Delay(50);
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

        internal async Task ParseMessageAsync(string message, Client source, MessageType type = MessageType.Chat)
        {
            if (!message.StartsWith('/'))
            {
                var chat = await this.Events.InvokeIncomingChatMessageAsync(new IncomingChatMessageEventArgs(source.Player, message));

                if(!chat.Cancel)
                    await this.BroadcastAsync($"<{source.Player.Username}> {message}", type);

                return;
            }

            // TODO command logging
            // TODO error handling for commands
            var context = new ObsidianContext(message, source.Player, this);
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
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.EntityId)))
                await player.client.QueuePacketAsync(packet);
        }

        internal async Task BroadcastPacketWithoutQueueAsync(IPacket packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.EntityId)))
                await player.client.SendPacketAsync(packet);
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
            this.Logger.LogDebug($"{entity.EntityId} new ID");

            return world.TryAddEntity(entity);
        }

        internal async Task BroadcastPlayerDigAsync(PlayerDiggingStore store)
        {
            var digging = store.Packet;

            var block = this.World.GetBlock(digging.Location);

            var player = this.OnlinePlayers.GetValueOrDefault(store.Player);

            switch (digging.Status)
            {
                case DiggingStatus.DropItem:
                    {
                        var droppedItem = player.GetHeldItem();

                        if (droppedItem is null || droppedItem.Type == Materials.Air)
                            return;

                        var loc = new Position(player.Location.X, player.HeadY - 0.3, player.Location.Z);

                        var item = new ItemEntity
                        {
                            EntityId = player + this.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = droppedItem.Id,
                            EntityBitMask = EntityBitMask.Glowing,
                            World = this.World,
                            Location = loc
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

                        await this.BroadcastPacketWithoutQueueAsync(new SpawnEntity
                        {
                            EntityId = item.EntityId,
                            Uuid = Guid.NewGuid(),
                            Type = EntityType.Item,
                            Position = item.Location,
                            Pitch = 0,
                            Yaw = 0,
                            Data = 1,
                            Velocity = vel
                        });
                        await this.BroadcastPacketWithoutQueueAsync(new EntityMetadata
                        {
                            EntityId = item.EntityId,
                            Entity = item
                        });

                        await player.client.SendPacketAsync(new SetSlot
                        {
                            Slot = player.CurrentSlot,

                            WindowId = 0,

                            SlotData = player.Inventory.GetItem(player.CurrentSlot) - 1
                        });

                        player.Inventory.RemoveItem(player.CurrentSlot);
                        break;
                    }
                case DiggingStatus.StartedDigging:
                    await this.BroadcastPacketWithoutQueueAsync(new AcknowledgePlayerDigging
                    {
                        Location = digging.Location,
                        Block = block.Id,
                        Status = digging.Status,
                        Successful = true
                    });
                    break;
                case DiggingStatus.CancelledDigging:
                    await this.BroadcastPacketWithoutQueueAsync(new AcknowledgePlayerDigging
                    {
                        Location = digging.Location,
                        Block = block.Id,
                        Status = digging.Status,
                        Successful = true
                    });
                    break;
                case DiggingStatus.FinishedDigging:
                    {
                        await this.BroadcastPacketWithoutQueueAsync(new AcknowledgePlayerDigging
                        {
                            Location = digging.Location,
                            Block = block.Id,
                            Status = digging.Status,
                            Successful = true
                        });
                        await this.BroadcastPacketWithoutQueueAsync(new BlockBreakAnimation
                        {
                            EntityId = player,
                            Location = digging.Location,
                            DestroyStage = -1
                        });

                        await this.BroadcastPacketWithoutQueueAsync(new BlockChange(digging.Location, 0));

                        this.World.SetBlock(digging.Location, null);

                        var item = new ItemEntity
                        {
                            EntityId = player + this.World.TotalLoadedEntities() + 1,
                            Count = 1,
                            Id = Registry.GetItem(block.Type).Id,
                            EntityBitMask = EntityBitMask.Glowing,
                            World = this.World,
                            Location = digging.Location.Add((Globals.Random.NextDouble() * 0.5F) + 0.25D,
                            (Globals.Random.NextDouble() * 0.5F) + 0.25D,
                            (Globals.Random.NextDouble() * 0.5F) + 0.25D)
                        };

                        this.TryAddEntity(player.World, item);

                        await this.BroadcastPacketWithoutQueueAsync(new SpawnEntity
                        {
                            EntityId = item.EntityId,
                            Uuid = Guid.NewGuid(),
                            Type = EntityType.Item,
                            Position = item.Location,
                            Pitch = 0,
                            Yaw = 0,
                            Data = 1,
                            Velocity = new Velocity((short)(digging.Location.X * (8000 / 20)), (short)(digging.Location.Y * (8000 / 20)), (short)(digging.Location.Z * (8000 / 20)))
                        });

                        await this.BroadcastPacketWithoutQueueAsync(new EntityMetadata
                        {
                            EntityId = item.EntityId,
                            Entity = item
                        });
                        break;
                    }
                default:
                    break;
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
                await Task.Delay(50);

                await this.Events.InvokeServerTickAsync();

                keepAliveTicks++;
                if (keepAliveTicks > 50)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var client in this.clients.Where(x => x.State == ClientState.Play))
                        _ = Task.Run(async () => await client.ProcessKeepAliveAsync(keepaliveid));

                    keepAliveTicks = 0;
                }

                foreach (var (uuid, player) in this.OnlinePlayers)
                {
                    if (this.Config.Baah.HasValue)
                    {
                        var soundPosition = new SoundPosition(player.Location.X, player.Location.Y, player.Location.Z);
                        await player.SendSoundAsync(Sounds.EntitySheepAmbient, soundPosition, SoundCategory.Master, 1.0f, 1.0f);
                    }

                    if (this.chatMessages.TryPeek(out QueueChat msg))
                        await player.SendMessageAsync(msg.Message, msg.Type);
                }

                this.chatMessages.TryDequeue(out var _);

                foreach (var client in clients)
                {
                    if (!client.tcp.Connected)
                        this.clients.TryRemove(client);
                }
                itersPerSecond++;

                // if Stopwatch elapsed time more than 1000 ms, reset counter, restart stopwatch, and set TPS property
                if (stopWatch.ElapsedMilliseconds >= 1000L)
                {
                    TPS = itersPerSecond;
                    itersPerSecond = 0;
                    stopWatch.Restart();
                }
            }
        }

        /// <summary>
        /// Registers the "obsidian-vanilla" entities and objects.
        /// </summary>
        private async Task RegisterDefaultAsync()
        {
            await this.RegisterAsync(new SuperflatGenerator());
            await this.RegisterAsync(new TestBlocksGenerator());
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
                    EntityId = joinedPlayer.EntityId,
                    Uuid = joinedPlayer.Uuid,
                    Position = joinedPlayer.Location,
                    Yaw = 0,
                    Pitch = 0
                });

                //await joined.client.QueuePacketAsync(new EntityMovement { EntityId = player.EntityId });
                await joinedPlayer.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = player.EntityId,
                    Uuid = player.Uuid,
                    Position = player.Location,
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

        private  Task OnServerTick() => Task.CompletedTask;

        #endregion Events

        struct QueueChat
        {
            public IChatMessage Message;
            public MessageType Type;
        }
    }
}
