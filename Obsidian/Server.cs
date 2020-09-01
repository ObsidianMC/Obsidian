using Newtonsoft.Json;
using Obsidian.Blocks;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Plugins;
using Obsidian.Sounds;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry;
using Obsidian.World;
using Obsidian.World.Generators;
using Qmmands;
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
        private readonly ConcurrentQueue<PlayerDigging> diggers;
        private readonly ConcurrentQueue<PlayerBlockPlacement> placed;
        private readonly ConcurrentHashSet<Client> clients;

        private readonly CancellationTokenSource cts;
        private readonly TcpListener tcpListener;

        public DateTimeOffset StartTime { get; private set; }

        public WorldGenerator WorldGenerator { get; private set; }

        public MinecraftEventHandler Events { get; }
        public PluginManager PluginManager { get; }

        public OperatorList Operators { get; }

        public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new ConcurrentDictionary<Guid, Player>();

        public List<WorldGenerator> WorldGenerators { get; } = new List<WorldGenerator>();

        public CommandService Commands { get; }
        public Config Config { get; }
        public AsyncLogger Logger { get; }

        public int TotalTicks { get; private set; }

        public int Id { get; }
        public string Version { get; }
        public int Port { get; }

        public World.World World { get; }

        public string ServerFolderPath => Path.GetFullPath($"Server-{this.Id}");

        /// <summary>
        /// Creates a new Server instance. Spawning multiple of these could make a multi-server setup  :thinking:
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            this.Logger = new AsyncLogger($"Obsidian ID: {serverId}", Program.Config.LogLevel, Path.Combine(ServerFolderPath, "latest.log"));

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;

            this.tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.clients = new ConcurrentHashSet<Client>();

            this.cts = new CancellationTokenSource();
            this.chatmessages = new ConcurrentQueue<QueueChat>();
            this.diggers = new ConcurrentQueue<PlayerDigging>();
            this.placed = new ConcurrentQueue<PlayerBlockPlacement>();
            this.Commands = new CommandService(new CommandServiceConfiguration()
            {
                CaseSensitive = false,
                DefaultRunMode = RunMode.Parallel,
                IgnoreExtraArguments = true
            });
            this.Commands.AddModule<MainCommandModule>();
            this.Commands.AddTypeParser(new LocationTypeParser());
            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(this);
            this.Operators = new OperatorList(this);

            this.World = new World.World("", this.WorldGenerator);

            this.Events.PlayerLeave += this.Events_PlayerLeave;
            this.Events.PlayerJoin += this.Events_PlayerJoin;
        }

        internal async Task BroadcastBlockPlacementAsync(Guid senderId, PlayerBlockPlacement pbp)
        {
            foreach (var (uuid, player) in this.OnlinePlayers.Where(x => x.Key != senderId))
            {
                var client = player.client;

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

                var b = new BlockChange(location, BlockRegistry.GetBlock(Materials.Cobblestone).Id);

                await client.SendBlockChangeAsync(b);
            }
        }

        internal async Task ParseMessage(string message, Client source, sbyte position = 0)
        {
            if (!CommandUtilities.HasPrefix(message, '/', out string output))
            {
                await this.BroadcastAsync($"<{source.Player.Username}> {message}", position);
                await Logger.LogMessageAsync($"<{source.Player.Username}> {message}");
                return;
            }

            var context = new CommandContext(source, this);
            IResult result = await Commands.ExecuteAsync(output, context);
            if (!result.IsSuccessful)
                await context.Player.SendMessageAsync($"{ChatColor.Red}Command error: {(result as FailedResult).Reason}", position);

        }

        /// <summary>
        /// Sends a message to all online players on the server
        /// </summary>
        public async Task BroadcastAsync(string message, sbyte position = 0)
        {
            this.chatmessages.Enqueue(new QueueChat() { Message = message, Position = position });
            await Logger.LogMessageAsync(message);
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        public async Task StartServer()
        {
            Console.CancelKeyPress += this.Console_CancelKeyPress;
            await this.Logger.LogMessageAsync($"Launching Obsidian Server v{Version} with ID {Id}");

            //Why?????
            //Check if MPDM and OM are enabled, if so, we can't handle connections 
            if (this.Config.MulitplayerDebugMode && this.Config.OnlineMode)
            {
                await this.Logger.LogErrorAsync("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                this.StopServer();
                return;
            }

            await this.Logger.LogDebugAsync("Registering blocks..");
            await BlockRegistry.RegisterAll();
            await ItemRegistry.RegisterAll();

            await this.Logger.LogMessageAsync($"Loading operator list...");
            this.Operators.Initialize();

            await this.Logger.LogMessageAsync("Registering default entities");
            await this.RegisterDefaultAsync();

            await this.Logger.LogMessageAsync($"Loading and Initializing plugins...");
            await this.PluginManager.LoadPluginsAsync(this.Logger);

            if (this.WorldGenerators.FirstOrDefault(g => g.Id == this.Config.Generator) is WorldGenerator worldGenerator)
            {
                this.WorldGenerator = worldGenerator;
            }
            else
            {
                await this.Logger.LogWarningAsync($"Generator ({this.Config.Generator}) is unknown. Using default generator");
                this.WorldGenerator = new SuperflatGenerator();
            }

            await this.Logger.LogMessageAsync($"World generator set to {this.WorldGenerator.Id} ({this.WorldGenerator})");

            await this.Logger.LogDebugAsync($"Set start DateTimeOffset for measuring uptime.");
            this.StartTime = DateTimeOffset.Now;

            await this.Logger.LogMessageAsync("Starting server backend...");
            await Task.Factory.StartNew(async () => { await this.ServerLoop().ConfigureAwait(false); });

            if (!this.Config.OnlineMode)
                await this.Logger.LogMessageAsync($"Server started in offline mode..");

            await this.Logger.LogDebugAsync($"Start listening for new clients");
            this.tcpListener.Start();

            while (!cts.IsCancellationRequested)
            {
                var tcp = await this.tcpListener.AcceptTcpClientAsync();

                await this.Logger.LogDebugAsync($"New connection from client with IP {tcp.Client.RemoteEndPoint}");

                int newplayerid = Math.Max(0, this.clients.Count);

                var clnt = new Client(tcp, this.Config, newplayerid, this);
                this.clients.Add(clnt);

                await Task.Factory.StartNew(async () => { await clnt.StartConnectionAsync().ConfigureAwait(false); });
            }

            await this.Logger.LogWarningAsync($"Cancellation has been requested. Stopping server...");
        }

        /// <summary>
        /// Registers a new entity to the server
        /// </summary>
        /// <param name="input">A compatible entry</param>
        /// <exception cref="Exception">Thrown if unknown/unhandable type has been passed</exception>
        public async Task RegisterAsync(params object[] input)
        {
            foreach (object item in input)
            {
                switch (item)
                {
                    default:
                        throw new Exception($"Input ({item.GetType()}) can't be handled by RegisterAsync.");

                    case WorldGenerator generator:
                        await Logger.LogDebugAsync($"Registering {generator.Id}...");
                        this.WorldGenerators.Add(generator);
                        break;
                }
            }
        }

        public async Task DisconnectIfConnectedAsync(string username, ChatMessage reason = null)
        {
            var player = this.OnlinePlayers.Values.FirstOrDefault(x => x.Username == username);
            if (player != null)
            {
                if (reason is null)
                    reason = ChatMessage.Simple("Connected from another location");

                await player.DisconnectAsync(reason);
            }
        }

        public bool IsPlayerOnline(string username) => this.clients.Any(x => x.Player != null && x.Player.Username == username);

        public void EnqueueDigging(PlayerDigging d) => this.diggers.Enqueue(d);

        public T LoadConfig<T>(Plugin plugin)
        {
            var path = plugin.GetConfigPath();

            if (!File.Exists(path))
                this.SaveConfig(plugin, default(T));

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public void SaveConfig(Plugin plugin, object config)
        {
            var path = plugin.GetConfigPath();
            var json = JsonConvert.SerializeObject(config);
            File.WriteAllText(path, json);
        }

        public void StopServer()
        {
            this.WorldGenerators.Clear(); //Clean up for memory and next boot
            this.cts.Cancel();

            foreach (var client in this.clients)
                client.Disconnect();

            Console.WriteLine("Cancewlled");
        }

        private async Task ServerLoop()
        {
            var keepaliveticks = 0;
            while (!this.cts.IsCancellationRequested)
            {
                await Task.Delay(50);

                this.TotalTicks++;
                await this.Events.InvokeServerTickAsync();

                keepaliveticks++;
                if (keepaliveticks > 50)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var clnt in this.clients.Where(x => x.State == ClientState.Play))
                        _ = Task.Run(async () => { await clnt.ProcessKeepAlive(keepaliveid); });

                    keepaliveticks = 0;
                }

                foreach (var (uuid, player) in this.OnlinePlayers)
                {
                    if (this.Config.Baah.HasValue)
                    {
                        var pos = new SoundPosition(player.Transform.X, player.Transform.Y, player.Transform.Z);
                        await player.SendSoundAsync(461, pos, SoundCategory.Master, 1.0f, 1.0f);
                    }

                    if (this.chatmessages.TryPeek(out QueueChat msg))
                        await player.SendMessageAsync(msg.Message, msg.Position);


                    if (this.diggers.TryPeek(out PlayerDigging d))
                    {
                        var b = new BlockChange(d.Location, BlockRegistry.GetBlock(Materials.Air).Id);

                        await player.client.SendBlockChangeAsync(b);
                    }
                }

                this.chatmessages.TryDequeue(out var _);
                this.diggers.TryDequeue(out var _);

                foreach (var client in clients)
                {
                    if (!client.tcp.Connected)
                    {
                        this.clients.TryRemove(client);

                        continue;
                    }
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

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            // TODO: TRY TO GRACEFULLY SHUT DOWN THE SERVER WE DONT WANT ERRORS REEEEEEEEEEE
            Console.WriteLine("shutting down..");
            this.StopServer();
        }

        #region events
        private async Task Events_PlayerLeave(PlayerLeaveEventArgs e)
        {
            //TODO same here :)
            foreach (var other in this.OnlinePlayers.Values.Except(new List<Player> { e.WhoLeft }))
                await other.client.RemovePlayerFromListAsync(e.WhoLeft);

            await this.BroadcastAsync(string.Format(this.Config.LeaveMessage, e.WhoLeft.Username));
        }

        private async Task Events_PlayerJoin(PlayerJoinEventArgs e)
        {
            //TODO do this from somewhere else
            await this.BroadcastAsync(string.Format(this.Config.JoinMessage, e.Joined.Username));
            foreach (var (_, other) in this.OnlinePlayers)
            {
                await other.client.AddPlayerToListAsync(e.Joined);
            }

            await this.SendSpawnPlayerAsync(e.Joined);
        }

        public async Task SendSpawnPlayerAsync(Player except)
        {
            await this.Logger.LogWarningAsync($"Received spawn player sending to other clients... {string.Join(", ", this.OnlinePlayers.Except(except).Select(x => x.Value.Username))}");
            var expectPacket = new EntityPacket
            {
                Id = except.client.id
            };

            foreach (var (_, player) in this.OnlinePlayers.Except(except))
            {
                await this.Logger.LogWarningAsync($"Sending to: {player.Username} \nExcluded: {except.Username}");

                await player.client.SpawnPlayerAsync(except);

                await except.client.SpawnPlayerAsync(player);//This is for now but we need to implement this properly one of these days :v
            }
        }

        #endregion
    }
}