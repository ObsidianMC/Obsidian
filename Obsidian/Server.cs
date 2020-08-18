using Newtonsoft.Json;
using Obsidian.BlockData;
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

namespace Obsidian
{
    public struct QueueChat
    {
        public string Message;
        public sbyte Position;
    }

    public class Server
    {
        private readonly ConcurrentQueue<QueueChat> _chatmessages;
        private readonly ConcurrentQueue<PlayerDigging> _diggers; // PETALUL this was unintended
        private readonly ConcurrentQueue<PlayerBlockPlacement> _placed;
        private ConcurrentHashSet<Client> _clients { get; }

        private readonly CancellationTokenSource _cts;
        private readonly TcpListener _tcpListener;

        public MinecraftEventHandler Events;
        public PluginManager PluginManager;
        public DateTimeOffset StartTime;

        public OperatorList Operators;

        public ConcurrentDictionary<string, Player> OnlinePlayers { get; } = new ConcurrentDictionary<string, Player>();

        public List<WorldGenerator> WorldGenerators { get; } = new List<WorldGenerator>();

        public WorldGenerator WorldGenerator { get; private set; }

        public CommandService Commands { get; }
        public Config Config { get; }
        public AsyncLogger Logger { get; }
        public int Id { get; private set; }
        public string Version { get; }
        public int Port { get; }
        public int TotalTicks { get; private set; }
        public World.World world;

        public string Path => System.IO.Path.GetFullPath(Id.ToString());

        /// <summary>
        /// Creates a new Server instance. Spawning multiple of these could make a multi-server setup  :thinking:
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, int serverId)
        {
            this.Config = config;

            this.Logger = new AsyncLogger($"Obsidian ID: {serverId}", Program.Config.LogLevel, System.IO.Path.Combine(Path, "latest.log"));

            this.Port = config.Port;
            this.Version = version;
            this.Id = serverId;

            this._tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this._clients = new ConcurrentHashSet<Client>();

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
            this.Commands.AddTypeParser(new LocationTypeParser());
            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(this);
            this.Operators = new OperatorList(this);

            this.world = new World.World("", WorldGenerator);

            this.Events.PlayerLeave += this.Events_PlayerLeave;
            this.Events.PlayerJoin += this.Events_PlayerJoin;
        }

        private async Task ServerLoop()
        {
            var keepaliveticks = 0;
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(50);

                TotalTicks++;
                await Events.InvokeServerTickAsync();

                keepaliveticks++;
                if (keepaliveticks > 50)
                {
                    var keepaliveid = DateTime.Now.Millisecond;

                    foreach (var clnt in this._clients.Where(x => x.State == ClientState.Play).ToList())
                        _ = Task.Run(async () => { await clnt.ProcessKeepAlive(keepaliveid); });
                    keepaliveticks = 0;
                }

                if (_chatmessages.Count > 0)
                {
                    if (_chatmessages.TryDequeue(out QueueChat msg))
                    {
                        foreach (var player in this.OnlinePlayers.Values)
                            _ = Task.Run(async () => { await player.SendMessageAsync(msg.Message, msg.Position); });
                    }
                }

                if (_diggers.Count > 0)
                {
                    if (_diggers.TryDequeue(out PlayerDigging d))
                    {
                        foreach (var clnt in _clients)
                        {
                            var b = new BlockChange(d.Location, BlockRegistry.G(Materials.Air).Id);

                            await clnt.SendBlockChangeAsync(b);
                        }
                    }
                }

                // TODO use blockface values to determine where block should be placed
                if (_placed.Count > 0)
                {
                    if (_placed.TryDequeue(out PlayerBlockPlacement pbp))
                    {
                        foreach (var clnt in _clients)
                        {
                            var location = pbp.Location;

                            var b = new BlockChange(pbp.Location, BlockRegistry.G(Materials.Cobblestone).Id);
                            await clnt.SendBlockChangeAsync(b);
                        }
                    }
                }

                if (Config.Baah.HasValue)
                {
                    foreach (var player in this.OnlinePlayers.Values)
                    {
                        var pos = new SoundPosition(player.Transform.X, player.Transform.Y, player.Transform.Z);
                        await player.SendSoundAsync(461, pos, SoundCategory.Master, 1.0f, 1.0f);
                    }
                }

                foreach (var client in _clients)
                {
                    if (!client.tcp.Connected)
                    {
                        this._clients.TryRemove(client);

                        continue;
                    }

                    //?
                    if (client.State == ClientState.Play)
                        await world.UpdateChunksForClientAsync(client);
                }
            }
        }

        public bool CheckPlayerOnline(string username) => this._clients.Any(x => x.Player != null && x.Player.Username == username);

        public void EnqueueDigging(PlayerDigging d)
        {
            _diggers.Enqueue(d);
        }

        public void EnqueuePlacing(PlayerBlockPlacement pbp)
        {
            _placed.Enqueue(pbp);
        }

        public T LoadConfig<T>(Plugin plugin)
        {
            var path = GetConfigPath(plugin);

            if (!System.IO.File.Exists(path))
                SaveConfig(plugin, default(T));

            var json = System.IO.File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public void SaveConfig(Plugin plugin, object config)
        {
            var path = GetConfigPath(plugin);
            var json = JsonConvert.SerializeObject(config);
            System.IO.File.WriteAllText(path, json);
        }

        private string GetConfigPath(Plugin plugin)
        {
            var path = plugin.Path;
            var folder = System.IO.Path.GetDirectoryName(path);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            return System.IO.Path.Combine(folder, fileName) + ".json";
        }

        public async Task ParseMessage(string message, Client source, sbyte position = 0)
        {
            if (!CommandUtilities.HasPrefix(message, '/', out string output))
            {
                _chatmessages.Enqueue(new QueueChat() { Message = $"<{source.Player.Username}> {message}", Position = position });
                await Logger.LogMessageAsync($"<{source.Player.Username}> {message}");
                return;
            }

            var context = new CommandContext(source, this);
            IResult result = await Commands.ExecuteAsync(output, context);
            if (!result.IsSuccessful)
            {
                await context.Player.SendMessageAsync($"{ChatColor.Red}Command error: {(result as FailedResult).Reason}", position);
            }
        }

        public async Task BroadcastAsync(string message, sbyte position = 0)
        {
            _chatmessages.Enqueue(new QueueChat() { Message = message, Position = position });
            await Logger.LogMessageAsync(message);
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        /// <returns></returns>
        public async Task StartServer()
        {
            Console.CancelKeyPress += this.Console_CancelKeyPress;
            await Logger.LogMessageAsync($"Launching Obsidian Server v{Version} with ID {Id}");

            //Why?????
            //Check if MPDM and OM are enabled, if so, we can't handle connections 
            if (Config.MulitplayerDebugMode && Config.OnlineMode)
            {
                await Logger.LogErrorAsync("Incompatible Config: Multiplayer debug mode can't be enabled at the same time as online mode since usernames will be overwritten");
                StopServer();
                return;
            }

            await Logger.LogDebugAsync("Registering blocks..");
            await BlockRegistry.RegisterAll();

            await Logger.LogMessageAsync($"Loading operator list...");
            Operators.Initialize();

            await Logger.LogMessageAsync("Registering default entities");
            await RegisterDefaultAsync();

            await Logger.LogMessageAsync($"Loading and Initializing plugins...");
            await this.PluginManager.LoadPluginsAsync(this.Logger);

            if (WorldGenerators.FirstOrDefault(g => g.Id == Config.Generator) is WorldGenerator worldGenerator)
            {
                this.WorldGenerator = worldGenerator;
            }
            else
            {
                await this.Logger.LogWarningAsync($"Generator ({Config.Generator}) is unknown. Using default generator");
                this.WorldGenerator = new SuperflatGenerator();
            }

            await Logger.LogMessageAsync($"World generator set to {this.WorldGenerator.Id} ({this.WorldGenerator.ToString()})");

            await Logger.LogDebugAsync($"Set start DateTimeOffset for measuring uptime.");
            this.StartTime = DateTimeOffset.Now;

            await Logger.LogMessageAsync("Starting server backend...");
            await Task.Factory.StartNew(async () => { await this.ServerLoop().ConfigureAwait(false); });

            if (!this.Config.OnlineMode)
                await Logger.LogMessageAsync($"Server started in offline mode..");

            await Logger.LogDebugAsync($"Start listening for new clients");
            _tcpListener.Start();

            while (!_cts.IsCancellationRequested)
            {
                var tcp = await _tcpListener.AcceptTcpClientAsync();

                await Logger.LogDebugAsync($"New connection from client with IP {tcp.Client.RemoteEndPoint.ToString()}");

                int newplayerid = Math.Max(0, this._clients.Count);

                var clnt = new Client(tcp, this.Config, newplayerid, this);
                _clients.Add(clnt);

                await Task.Factory.StartNew(async () => { await clnt.StartConnectionAsync().ConfigureAwait(false); });
            }

            await Logger.LogWarningAsync($"Cancellation has been requested. Stopping server...");
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
        private async Task RegisterDefaultAsync()
        {
            await RegisterAsync(new SuperflatGenerator());
            await RegisterAsync(new TestBlocksGenerator());
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
                        WorldGenerators.Add(generator);
                        break;
                }
            }
        }

        #region events
        private async Task Events_PlayerLeave(PlayerLeaveEventArgs e)
        {
            //TODO same here :)
            foreach (var other in this.OnlinePlayers.Values.Except(new List<Player> { e.WhoLeft }))
                await other._client.RemovePlayerFromListAsync(e.WhoLeft);

            await this.BroadcastAsync(string.Format(this.Config.LeaveMessage, e.WhoLeft.Username));
        }

        private async Task Events_PlayerJoin(PlayerJoinEventArgs e)
        {
            //TODO do this from somewhere else
            foreach (var other in this.OnlinePlayers.Values.Except(new List<Player> { e.Joined }))
                await other._client.AddPlayerToListAsync(e.Joined);
            
            await this.BroadcastAsync(string.Format(this.Config.JoinMessage, e.Joined.Username));
        }

        #endregion
    }
}