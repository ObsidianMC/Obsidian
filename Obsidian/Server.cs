using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.Blocks;
using Obsidian.Chat;
using Obsidian.ChunkData;
using Obsidian.CommandFramework;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Items;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Plugins;
using Obsidian.Sounds;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Debug;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian
{
    public struct QueueChat
    {
        public string Message;
        public sbyte Position;
    }

    public class Server
    {
        private readonly ConcurrentQueue<QueueChat> chatmessages;
        private readonly ConcurrentQueue<PlayerBlockPlacement> placed;
        private readonly ConcurrentHashSet<Client> clients;

        private readonly CancellationTokenSource cts;
        private readonly TcpListener tcpListener;

        public IServiceProvider Services { get; private set; } = new ServiceCollection().BuildServiceProvider(true);

        public DateTimeOffset StartTime { get; private set; }

        public MinecraftEventHandler Events { get; }
        public PluginManager PluginManager { get; }

        public OperatorList Operators { get; }

        internal ConcurrentDictionary<int, Inventory> CachedWindows { get; } = new ConcurrentDictionary<int, Inventory>();

        public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new ConcurrentDictionary<Guid, Player>();

        public ConcurrentDictionary<string, World> Worlds { get; private set; } = new ConcurrentDictionary<string, World>();

        public Dictionary<string, WorldGenerator> WorldGenerators { get; } = new Dictionary<string, WorldGenerator>();

        public HashSet<string> RegisteredChannels { get; private set; } = new HashSet<string>();        

        public CommandHandler Commands { get; }
        public Config Config { get; }

        public ILogger Logger { get; }

        public LoggerProvider LoggerProvider { get; }

        public int TotalTicks { get; private set; }

        public int Id { get; }
        public string Version { get; }
        public int Port { get; }

        public World World { get; }

        public string ServerFolderPath => Path.GetFullPath($"Server-{this.Id}");

        /// <summary>
        /// Creates a new Server instance.
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            this.LoggerProvider = new LoggerProvider(LogLevel.Debug);
            this.Logger = this.LoggerProvider.CreateLogger($"Server/{this.Id}");
            //This stuff down here needs to be looked into
            Program.PacketLogger = this.LoggerProvider.CreateLogger("Packets");
            PacketDebug.Logger = this.LoggerProvider.CreateLogger("PacketDebug");
            Registry.Logger = this.LoggerProvider.CreateLogger("Registry");

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;

            this.tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.clients = new ConcurrentHashSet<Client>();

            this.cts = new CancellationTokenSource();

            this.chatmessages = new ConcurrentQueue<QueueChat>();
            this.placed = new ConcurrentQueue<PlayerBlockPlacement>();


            this.Commands = new CommandHandler("/");
            this.Commands.RegisterCommandClass<MainCommandModule>();
            this.Commands.AddArgumentParser(new LocationTypeParser());
            this.Commands.AddArgumentParser(new PlayerTypeParser());
            this.Commands.RegisterContextType<ObsidianContext>();


            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(this);
            this.Operators = new OperatorList(this);

            this.World = new World("", this);

            this.Events.PlayerLeave += this.Events_PlayerLeave;
            this.Events.PlayerJoin += this.Events_PlayerJoin;
            this.Events.ServerTick += this.Events_ServerTick;
        }

        /// <summary>
        /// Checks if a player is online
        /// </summary>
        /// <param name="username">The username you want to check for</param>
        /// <returns>True if the player is online</returns>
        public bool IsPlayerOnline(string username) => this.OnlinePlayers.Any(x => x.Value.Username == username);

        public bool IsPlayerOnline(Guid uuid) => this.OnlinePlayers.ContainsKey(uuid);

        /// <summary>
        /// Sends a message to all online players on the server
        /// </summary>
        public Task BroadcastAsync(string message, sbyte position = 0)
        {
            this.chatmessages.Enqueue(new QueueChat() { Message = message, Position = position });
            this.Logger.LogInformation(message);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers a new entity to the server
        /// </summary>
        /// <param name="input">A compatible entry</param>
        /// <exception cref="Exception">Thrown if unknown/unhandable type has been passed</exception>
        public Task RegisterAsync(params object[] input)
        {
            foreach (object item in input)
            {
                switch (item)
                {
                    default:
                        throw new Exception($"Input ({item.GetType()}) can't be handled by RegisterAsync.");

                    case WorldGenerator generator:
                        Logger.LogDebug($"Registering {generator.Id}...");
                        this.WorldGenerators.Add(generator.Id, generator);
                        break;
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        public async Task StartServer()
        {
            this.StartTime = DateTimeOffset.Now;

            this.Logger.LogInformation($"Launching Obsidian Server v{Version} with ID {Id}");

            //Check if MPDM and OM are enabled, if so, we can't handle connections
            if (this.Config.MulitplayerDebugMode && this.Config.OnlineMode)
            {
                this.Logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                this.StopServer();
                return;
            }

            await Registry.RegisterBlocksAsync();
            await Registry.RegisterItemsAsync();
            await Registry.RegisterBiomesAsync();

            this.Logger.LogInformation("Loading services..");
            //TODO services
            this.Services = new ServiceCollection()
                .AddSingleton(this.LoggerProvider)
                .BuildServiceProvider(true);

            this.Logger.LogInformation($"Loading properties...");
            await this.Operators.InitializeAsync();
            await this.RegisterDefaultAsync();

            this.Logger.LogInformation("Loading plugins...");
            await this.PluginManager.LoadPluginsAsync(this.Logger);

            if (!this.WorldGenerators.TryGetValue(this.Config.Generator, out WorldGenerator value))
                this.Logger.LogWarning($"Unknown generator type {this.Config.Generator}");

            this.World.Generator = value ?? new SuperflatGenerator();

            this.Logger.LogInformation($"World generator set to {this.World.Generator.Id} ({this.World.Generator})");

            this.World.GenerateWorld();

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

                    var clnt = new Client(tcp, this.Config, Math.Max(0, this.clients.Count + this.World.Entities.Count), this);
                    this.clients.Add(clnt);

                    _ = Task.Run(clnt.StartConnectionAsync);
                }
                await Task.Delay(50);
            }

            this.Logger.LogWarning("Server is shutting down...");
        }

        internal async Task BroadcastBlockPlacementAsync(Player player, PlayerBlockPlacement pbp)
        {
            foreach (var (_, other) in this.OnlinePlayers.Except(player))
            {
                var client = other.client;

                var location = pbp.Location;
                var face = pbp.Face;

                switch (face)
                {
                    case BlockFace.Bottom:
                        location.Y -= 1;
                        break;

                    case BlockFace.Top:
                        location.Y += 1;
                        break;

                    case BlockFace.North:
                        location.Z -= 1;
                        break;

                    case BlockFace.South:
                        location.Z += 1;
                        break;

                    case BlockFace.West:
                        location.X -= 1;
                        break;

                    case BlockFace.East:
                        location.X += 1;
                        break;

                    default:
                        break;
                }

                player.Inventory.RemoveItem(player.CurrentSlot);
                await client.QueuePacketAsync(new BlockChange(location, Registry.GetBlock(player.GetHeldItem().Type).Id));
            }
        }

        internal async Task ParseMessage(string message, Client source, sbyte position = 0)
        {
            if (!message.StartsWith('/'))
            {
                await this.BroadcastAsync($"<{source.Player.Username}> {message}", position);
                return;
            }

            //TODO command logging
            // TODO error handling for commands
            var context = new ObsidianContext(message, source, this);
            try
            {
                await Commands.ProcessCommand(context);
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        internal async Task BroadcastPacketAsync(Packet packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.EntityId)))
                await player.client.QueuePacketAsync(packet);
        }

        internal async Task BroadcastPacketWithoutQueueAsync(Packet packet, params int[] excluded)
        {
            foreach (var (_, player) in this.OnlinePlayers.Where(x => !excluded.Contains(x.Value.EntityId)))
                await player.client.SendPacketAsync(packet);
        }

        internal async Task DisconnectIfConnectedAsync(string username, ChatMessage reason = null)
        {
            var player = this.OnlinePlayers.Values.FirstOrDefault(x => x.Username == username);
            if (player != null)
            {
                if (reason is null)
                    reason = ChatMessage.Simple("Connected from another location");

                await player.KickAsync(reason);
            }
        }

        internal async Task BroadcastBlockBreakAsync(PlayerDiggingStore store)
        {
            var d = store.Packet;
            var airBlock = Registry.GetBlock(Materials.Air).Id;
            var minedBlock = this.World.GetBlock(d.Location);

            var player = this.OnlinePlayers.GetValueOrDefault(store.Player);

            switch (d.Status)
            {
                case DiggingStatus.StartedDigging:
                    await this.BroadcastPacketWithoutQueueAsync(new AcknowledgePlayerDigging
                    {
                        Location = d.Location,
                        Block = minedBlock.Id,
                        Status = d.Status,
                        Successful = true
                    });
                    break;
                case DiggingStatus.CancelledDigging:
                    await this.BroadcastPacketWithoutQueueAsync(new AcknowledgePlayerDigging
                    {
                        Location = d.Location,
                        Block = minedBlock.Id,
                        Status = d.Status,
                        Successful = true
                    });
                    break;
                case DiggingStatus.FinishedDigging:
                {
                    await this.BroadcastPacketWithoutQueueAsync(new AcknowledgePlayerDigging
                    {
                        Location = d.Location,
                        Block = minedBlock.Id,
                        Status = d.Status,
                        Successful = true
                    });
                    await this.BroadcastPacketWithoutQueueAsync(new BlockBreakAnimation
                    {
                        EntityId = player,
                        Location = d.Location,
                        DestroyStage = -1
                    });

                    await this.BroadcastPacketWithoutQueueAsync(new BlockChange(d.Location, airBlock));

                    var item = new ItemEntity
                    {
                        EntityId = player + this.World.Entities.Count + 1,
                        Count = 1,
                        Id = Registry.GetItem(minedBlock.Type).Id,
                        EntityBitMask = EntityBitMask.Glowing,
                        World = this.World,
                        Location = d.Location.Add((Program.Random.NextDouble() * 0.5F) + 0.25D,
                        (Program.Random.NextDouble() * 0.5F) + 0.25D,
                        (Program.Random.NextDouble() * 0.5F) + 0.25D)
                    };

                    this.World.Entities.Add(item);
                    this.Logger.LogDebug($"{item.EntityId} new ID");

                    await this.BroadcastPacketWithoutQueueAsync(new SpawnEntity
                    {
                        EntityId = item.EntityId,
                        Uuid = Guid.NewGuid(),
                        Type = EntityType.Item,
                        Position = item.Location,
                        Pitch = 0,
                        Yaw = 0,
                        Data = 1,
                        Velocity = new Velocity((short)(d.Location.X * (8000 / 20)), (short)(d.Location.Y * (8000 / 20)), (short)(d.Location.Z * (8000 / 20)))
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
            var keepaliveticks = 0;
            while (!this.cts.IsCancellationRequested)
            {
                await Task.Delay(50);

                await this.Events.InvokeServerTickAsync();

                keepaliveticks++;
                if (keepaliveticks > 50)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var clnt in this.clients.Where(x => x.State == ClientState.Play))
                        _ = Task.Run(async () => await clnt.ProcessKeepAlive(keepaliveid));

                    keepaliveticks = 0;
                }

                foreach (var (uuid, player) in this.OnlinePlayers)
                {
                    if (this.Config.Baah.HasValue)
                    {
                        var pos = new SoundPosition(player.Location.X, player.Location.Y, player.Location.Z);
                        await player.SendSoundAsync(461, pos, SoundCategory.Master, 1.0f, 1.0f);
                    }

                    if (this.chatmessages.TryPeek(out QueueChat msg))
                        await player.SendMessageAsync(msg.Message, msg.Position);
                }

                this.chatmessages.TryDequeue(out var _);

                foreach (var client in clients)
                {
                    if (!client.tcp.Connected)
                        this.clients.TryRemove(client);
                }
            }
        }

        /// <summary>
        /// Registers the "obsidian-vanilla" entities and objects
        /// </summary>
        private async Task RegisterDefaultAsync()
        {
            await this.RegisterAsync(new SuperflatGenerator());
            await this.RegisterAsync(new TestBlocksGenerator());
        }

        private async Task SendSpawnPlayerAsync(Player joined)
        {
            foreach (var (_, player) in this.OnlinePlayers.Except(joined))
            {
                //await player.client.QueuePacketAsync(new EntityMovement { EntityId = joined.EntityId });
                await player.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = joined.EntityId,
                    Uuid = joined.Uuid,
                    Position = joined.Location,
                    Yaw = 0,
                    Pitch = 0
                });

                //await joined.client.QueuePacketAsync(new EntityMovement { EntityId = player.EntityId });
                await joined.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = player.EntityId,
                    Uuid = player.Uuid,
                    Position = player.Location,
                    Yaw = 0,
                    Pitch = 0
                });
            }
        }

        #region events

        private async Task Events_PlayerLeave(PlayerLeaveEventArgs e)
        {
            foreach (var (_, other) in this.OnlinePlayers.Except(e.Player))
                await other.client.RemovePlayerFromListAsync(e.Player);

            await this.BroadcastAsync(string.Format(this.Config.LeaveMessage, e.Player.Username));
        }

        private async Task Events_PlayerJoin(PlayerJoinEventArgs e)
        {
            var joined = e.Player;
            await this.BroadcastAsync(string.Format(this.Config.JoinMessage, e.Player.Username));
            foreach (var (_, other) in this.OnlinePlayers)
                await other.client.AddPlayerToListAsync(joined);

            //Need a delay here or players start flying
            await Task.Delay(500);
            await this.SendSpawnPlayerAsync(joined);
        }

        private async Task Events_ServerTick()
        {
            foreach (var ents in this.World.Entities)
                await ents.TickAsync();
        }

        #endregion events
    }
}