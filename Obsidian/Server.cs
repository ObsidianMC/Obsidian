using Newtonsoft.Json;
using Obsidian.BlockData;
using Obsidian.Commands;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Plugins;
using Obsidian.Util;
using Obsidian.Util.Registry;
using Obsidian.World;
using Obsidian.World.Generators;
using Qmmands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlockFace = Obsidian.Util.DataTypes.BlockFace;

namespace Obsidian
{
    public struct QueueChat
    {
        public string Message;
        public byte Position;
    }

    public class Server
    {
        private readonly ConcurrentQueue<QueueChat> _chatmessages;
        private readonly ConcurrentQueue<PlayerDigging> _diggers; // PETALUL this was unintended
        private readonly ConcurrentQueue<PlayerBlockPlacement> _placed;

        private readonly CancellationTokenSource _cts;
        private readonly TcpListener _tcpListener;

        public MinecraftEventHandler Events;
        public PluginManager PluginManager;
        public DateTimeOffset StartTime;

        public OperatorList Operators;

        public List<WorldGenerator> WorldGenerators { get; } = new List<WorldGenerator>();

        public WorldGenerator WorldGenerator { get; private set; }

        public string Path => System.IO.Path.GetFullPath($"Server{Id}");

        /// <summary>
        /// Creates a new Server instance. Spawning multiple of these could make a multi-server setup  :thinking:
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            this.Logger = new Logger($"Obsidian ID: {serverId}", Program.Config.LogLevel);

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;

            this._tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.Clients = new ConcurrentHashSet<Client>();

            this._cts = new CancellationTokenSource();
            this._chatmessages = new ConcurrentQueue<QueueChat>();
            this._diggers = new ConcurrentQueue<PlayerDigging>();
            this._placed = new ConcurrentQueue<PlayerBlockPlacement>();
            this.Commands = new CommandService(new CommandServiceConfiguration()
            {
                CaseSensitive = false,
                DefaultRunMode = RunMode.Parallel,
                IgnoreExtraArguments = true
            });
            this.Commands.AddModule<MainCommandModule>();
            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(this);
            this.Operators = new OperatorList(this);

            this.world = new Entities.World("", WorldGenerator);
        }

        public ConcurrentHashSet<Client> Clients { get; }

        public List<Player> OnlinePlayers => Clients.Where(c => c.IsPlaying).Select(c => c.Player).ToList();

        public CommandService Commands { get; }
        public Config Config { get; }
        public Logger Logger { get; }
        public int Id { get; private set; }
        public string Version { get; }
        public int Port { get; }
        public int TotalTicks { get; private set; } = 0;
        public Entities.World world;

        private async Task ServerLoop()
        {
            var keepaliveticks = 0;
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(50);

                TotalTicks++;
                await Events.InvokeServerTick();

                keepaliveticks++;
                if (keepaliveticks > 200)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var clnt in this.Clients.Where(x => x.State == ClientState.Play).ToList())
                        await Task.Factory.StartNew(async () => { await clnt.ProcessKeepAlive(keepaliveid); });
                    keepaliveticks = 0;
                }

                if (_chatmessages.Count > 0)
                {
                    foreach (var players in this.OnlinePlayers)
                    {
                        if (_chatmessages.TryPeek(out QueueChat msg))
                            await Task.Factory.StartNew(async () => { await players.SendMessageAsync(msg.Message, msg.Position); });
                    }
                    _chatmessages.TryDequeue(out QueueChat chat);
                }

                if (_diggers.Count > 0)
                {
                    if (_diggers.TryPeek(out PlayerDigging d))
                    {
                        foreach (var clnt in Clients)
                        {
                            var b = new BlockChange(d.Location, BlockRegistry.G(Materials.Air).Id);

                            await clnt.SendBlockChangeAsync(b);
                        }
                    }
                    _diggers.TryDequeue(out PlayerDigging dd);
                }

                // (DONE ?) TODO use blockface values to determine where block should be placed 
                if (_placed.Count > 0)
                {
                    if (_placed.TryPeek(out PlayerBlockPlacement pbp))
                    {
                        foreach (var clnt in Clients)
                        {
                            var location = pbp.Location;
                            var face = pbp.Face;

                            BlockChange blockChange;
                            Block block = BlockRegistry.G(Materials.Cobblestone);

                            #if DEBUG
                            switch (clnt.Player.HeldItemSlot) {
                                case 0:
                                    block = BlockRegistry.G(Materials.Cobblestone);
                                    break;
                                case 1:
                                    block = BlockRegistry.G(Materials.Dirt);
                                    break;
                                case 2:
                                    block = BlockRegistry.G(Materials.Glass);
                                    break;
                                case 3:
                                    block = BlockRegistry.G(Materials.SpruceLog);
                                    break;
                                case 4:
                                    block = BlockRegistry.G(Materials.Bricks);
                                    break;
                                case 5:
                                    block = BlockRegistry.G(Materials.IronBlock);
                                    break;
                                case 6:
                                    block = BlockRegistry.G(Materials.SpruceLeaves);
                                    break;
                                case 7:
                                    block = BlockRegistry.G(Materials.Bedrock);
                                    break;
                            }
                            #endif
                            
                            switch (face) {
                                case BlockFace.Top:
                                    blockChange = new BlockChange(new Position(location.X, location.Y + 1, location.Z), block.Id);
                                    break;
                                case BlockFace.Bottom:
                                    blockChange = new BlockChange(new Position(location.X, location.Y - 1, location.Z), block.Id);
                                    break;
                                case BlockFace.East:
                                    blockChange = new BlockChange(new Position(location.X + 1, location.Y, location.Z), block.Id);
                                    break;
                                case BlockFace.West:
                                    blockChange = new BlockChange(new Position(location.X - 1, location.Y, location.Z), block.Id);
                                    break;
                                case BlockFace.South:
                                    blockChange = new BlockChange(new Position(location.X, location.Y, location.Z + 1), block.Id);
                                    break;
                                case BlockFace.North:
                                    blockChange = new BlockChange(new Position(location.X, location.Y, location.Z - 1), block.Id);
                                    break;
                                default:
                                    blockChange = new BlockChange(location, block.Id);
                                    break;
                            }
                            await clnt.SendBlockChangeAsync(blockChange);
                        }
                    }

                    _placed.TryDequeue(out PlayerBlockPlacement pbpn);
                }

                if (Config.Baah.HasValue)
                {
                    foreach (var player in this.OnlinePlayers)
                    {
                        var pos = new Position(player.Transform.X * 8, player.Transform.Y * 8, player.Transform.Z * 8);
                        await player.SendSoundAsync(461, pos, SoundCategory.Master, 1.0f, 1.0f);
                    }
                }

                foreach (var client in Clients)
                {
                    if (!client.Tcp.Connected)
                    {
                        this.Clients.TryRemove(client);
                        continue;
                    }

                    if (client.State == ClientState.Play) {
                        await world.UpdateChunksForClient(client);
                        
                    }
                }
            }
        }

        public bool CheckPlayerOnline(string username) => this.Clients.Any(x => x.Player != null && x.Player.Username == username);

        public void EnqueueDigging(PlayerDigging d)
        {
            _diggers.Enqueue(d);
        }

        public void EnqueuePlacing(PlayerBlockPlacement pbp)
        {
            _placed.Enqueue(pbp);
        }

        public T LoadConfig<T>(IPluginClass plugin)
        {
            string path = GetPath(plugin);

            if (!System.IO.File.Exists(path))
            {
                SaveConfig(plugin, default(T));
            }

            string json = System.IO.File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public void SaveConfig(IPluginClass plugin, object config)
        {
            string path = GetPath(plugin);
            string json = JsonConvert.SerializeObject(config);
            System.IO.File.WriteAllText(path, json);
        }

        private string GetPath(IPluginClass plugin)
        {
            string path = plugin.GetType().Assembly.Location;
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileNameWithoutExtension(path) + ".json");
        }

        public async Task SendNewPlayer(int id, Guid uuid, Transform position)
        {
            foreach (var clnt in this.Clients.Where(x => x.State == ClientState.Play).ToList())
            {
                if (clnt.PlayerId == id)
                    continue;

                await clnt.SendEntity(new EntityPacket { Id = id });
                await clnt.SendPlayerAsync(id, uuid, position);
            }
        }

        public async Task SendNewPlayer(int id, string uuid, Transform position)
        {
            foreach (var clnt in this.Clients.Where(x => x.State == ClientState.Play).ToList())
            {
                if (clnt.PlayerId == id)
                    continue;
                await clnt.SendEntity(new EntityPacket { Id = id });
                await clnt.SendPlayerAsync(id, uuid, position);
            }
        }

        public async Task AddPlayer(int id)
        {
            foreach (var clnt in this.Clients.Where(x => x.PlayerId != id))
            {
                if (clnt.PlayerId == id)
                {
                    Logger.LogError($"YOure not suppose to get this packet :( {id}");
                    continue;
                }

                await clnt.SendPlayerInfoAsync();
            }
        }

        public async Task ParseMessage(string message, Client source, byte position = 0)
        {
            if (!CommandUtilities.HasPrefix(message, '/', out string output))
            {
                _chatmessages.Enqueue(new QueueChat() { Message = $"<{source.Player.Username}> {message}", Position = position });
                Logger.LogMessage($"<{source.Player.Username}> {message}");
                return;
            }

            var context = new CommandContext(source, this);
            IResult result = await Commands.ExecuteAsync(output, context);
            if (!result.IsSuccessful)
            {
                await context.Player.SendMessageAsync($"{ChatColor.Red}Command error: {(result as FailedResult).Reason}", position);
            }
        }

        public void Broadcast(string message, byte position = 0)
        {
            _chatmessages.Enqueue(new QueueChat() { Message = message, Position = position });
            Logger.LogMessage(message);
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        /// <returns></returns>
        public async Task StartServer()
        {
            Console.CancelKeyPress += this.Console_CancelKeyPress;
            Logger.LogMessage($"Launching Obsidian Server v{Version} with ID {Id}");

            //Check if MPDM and OM are enabled, if so, we can't handle connections
            if (Config.MulitplayerDebugMode && Config.OnlineMode)
            {
                Logger.LogError("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                StopServer();
                return;
            }

            Logger.LogMessage($"Loading operator list...");
            Operators.Initialize();

            Logger.LogMessage("Registering default entities");
            RegisterDefault();

            Logger.LogMessage($"Loading and Initializing plugins...");
            await this.PluginManager.LoadPluginsAsync(this.Logger);

            if (WorldGenerators.FirstOrDefault(g => g.Id == Config.Generator) is WorldGenerator worldGenerator)
            {
                this.WorldGenerator = worldGenerator;
            }
            else
            {
                this.Logger.LogWarning($"Generator ({Config.Generator}) is unknown. Using default generator");
                this.WorldGenerator = new SuperflatGenerator();
            }

            Logger.LogMessage($"World generator set to {this.WorldGenerator.Id} ({this.WorldGenerator.ToString()})");

            Logger.LogDebug($"Set start DateTimeOffset for measuring uptime.");
            this.StartTime = DateTimeOffset.Now;

            Logger.LogMessage("Starting server backend...");
            await Task.Factory.StartNew(async () => { await this.ServerLoop().ConfigureAwait(false); });

            if (!this.Config.OnlineMode)
                this.Logger.LogMessage($"Server started in offline mode..");

            Logger.LogDebug($"Start listening for new clients");
            _tcpListener.Start();

            await BlockRegistry.RegisterAll();

            while (!_cts.IsCancellationRequested)
            {
                var tcp = await _tcpListener.AcceptTcpClientAsync();

                Logger.LogDebug($"New connection from client with IP {tcp.Client.RemoteEndPoint.ToString()}");

                int newplayerid = Math.Max(0, this.Clients.Count);

                var clnt = new Client(tcp, this.Config, newplayerid, this);
                Clients.Add(clnt);

                await Task.Factory.StartNew(async () => { await clnt.StartConnectionAsync().ConfigureAwait(false); });
            }
            Logger.LogWarning($"Cancellation has been requested. Stopping server...");
        }

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            // TODO: TRY TO GRACEFULLY SHUT DOWN THE SERVER WE DONT WANT ERRORS REEEEEEEEEEE
            Console.WriteLine("shutting down..");
            StopServer();
        }

        public void StopServer()
        {
            this.WorldGenerators.Clear(); //Clean up for memory and next boot
            this._cts.Cancel();
        }

        /// <summary>
        /// Registers the "obsidian-vanilla" entities and objects
        /// </summary>
        private void RegisterDefault()
        {
            Register(new SuperflatGenerator());
            Register(new TestBlocksGenerator());
        }

        /// <summary>
        /// Registers a new entity to the server
        /// </summary>
        /// <param name="input">A compatible entry</param>
        /// <exception cref="Exception">Thrown if unknown/unhandable type has been passed</exception>
        public void Register(params object[] input)
        {
            foreach (object item in input)
            {
                switch (item)
                {
                    default:
                        throw new Exception($"Input ({item.GetType().ToString()}) can't be handled by RegisterAsync.");

                    case WorldGenerator generator:
                        Logger.LogDebug($"Registering {generator.Id}...");
                        WorldGenerators.Add(generator);
                        break;
                }
            }
        }
    }
}