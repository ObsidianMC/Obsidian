using Obsidian.Commands;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Logging;
using Obsidian.Plugins;
using Qmmands;
using System;
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
        public byte Position;
    }

    public class Server
    {
        private ConcurrentHashSet<List<QueueChat>> _chatmessages;
        private CancellationTokenSource _cts;
        private TcpListener _tcpListener;

        private int keepaliveticks = 0;
        private int lastSentPingPacket = 0;
        private int lastPingTime = 0;

        public MinecraftEventHandler Events;
        public PluginManager PluginManager;
        public DateTimeOffset StartTime;

        /// <summary>
        /// Creates a new Server instance. Spawning multiple of these could make a multi-server setup  :thinking:
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, string serverid)
        {
            this.Logger = new Logger($"Obsidian ID: {serverid}");

            this.Config = config;
            this.Port = config.Port;
            this.Version = version;
            this.Id = serverid;

            this._tcpListener = new TcpListener(IPAddress.Any, this.Port);

            this.Clients = new ConcurrentHashSet<Client>();

            this._cts = new CancellationTokenSource();
            this._chatmessages = new ConcurrentHashSet<List<QueueChat>>();
            this.Commands = new CommandService(new CommandServiceConfiguration()
            {
                CaseSensitive = false,
                DefaultRunMode = RunMode.Parallel,
                IgnoreExtraArguments = true
            });
            this.Commands.AddModule<MainCommandModule>();
            this.Events = new MinecraftEventHandler();

            this.PluginManager = new PluginManager(this);        
        }

        public ConcurrentHashSet<Client> Clients { get; }
        public CommandService Commands { get; }
        public Config Config { get; }
        public Logger Logger { get; }
        public string Id { get; private set; }
        public string Version { get; }
        public int Port { get; }
        public int TotalTicks { get; private set; } = 0;

        

        private async Task ServerLoop()
        {
            // start loop
            while (!_cts.IsCancellationRequested)
            {
                // Loop shit
                await Task.Delay(50);

                TotalTicks++;
                await Events.InvokeServerTick();

                keepaliveticks++;
                if (keepaliveticks - lastSentPingPacket > 40)
                {
                    var keepaliveid = DateTime.Now.Ticks;

                    lastSentPingPacket = keepaliveticks;
                    lastPingTime = DateTime.Now.Millisecond;

                    if (this.Clients.Any(c => c.State == PacketState.Play))
                    {
                        
                        await Logger.LogMessageAsync($"Broadcasting keepalive {keepaliveid}");
                        foreach (var clnt in this.Clients)
                        {
                            if (clnt.State == PacketState.Play)
                            {
                                await Task.Factory.StartNew(async () => { await clnt.SendKeepAliveAsync(keepaliveid); });
                            }
                        }
                    }

                    keepaliveticks = 0;
                }

                // Chat
                if (_chatmessages.Count > 0)
                {
                    var msg = _chatmessages.First();
                    foreach (var clnt in this.Clients)
                    {
                        if (clnt.State == PacketState.Play)
                        {
                            foreach (var m in msg)
                            {
                                await Task.Factory.StartNew(async () => { await clnt.SendChatAsync(m.Message, m.Position); });
                            }
                        }
                    }
                    _chatmessages.TryRemove(msg);
                }

                foreach (var client in Clients)
                {
                    if (client.Timedout)
                    {
                        client.Disconnect();
                    }
                    if (!client.Tcp.Connected)
                    {
                        this.Clients.TryRemove(client);
                    }
                }
            }
        }

        public bool CheckPlayerOnline(string username) => this.Clients.Any(x => x.Player != null && x.Player.Username == username);

        public async Task SendChatAsync(string message, Client source, byte position = 0, bool system = false)
        {
            // if author is null that means chat is sent by system.
            if (system)
            {
                _chatmessages.Add(new List<QueueChat> { new QueueChat() { Message = message, Position = position } });
                await Logger.LogMessageAsync(message);
            }
            else
            {
                if (!CommandUtilities.HasPrefix(message, '/', out string output))
                {
                    string formattedmsg = $"<{source.Player.Username}> {message}";
                    _chatmessages.Add(new List<QueueChat> { new QueueChat() { Message = formattedmsg, Position = position } });
                    await Logger.LogMessageAsync(formattedmsg);
                    return;
                }

                var context = new CommandContext(source, this);
                IResult result = await Commands.ExecuteAsync(output, context);
                if (!result.IsSuccessful)
                    _chatmessages.Add(new List<QueueChat> { new QueueChat() { Message = $"{MinecraftColor.Red}Command error: {(result as FailedResult).Reason}",
                        Position = position } });
            }
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        /// <returns></returns>
        public async Task StartServer()
        {
            await Logger.LogMessageAsync($"Launching Obsidian Server v {Version} with ID {Id}");

            await Logger.LogMessageAsync($"Loading and Initializing plugins...");
            await this.PluginManager.LoadPluginsAsync(this.Logger);

            await Logger.LogMessageAsync($"Set start DateTimeOffset for measuring uptime.");
            this.StartTime = DateTimeOffset.Now;

            await Logger.LogMessageAsync("Starting server backend...");
            await Task.Factory.StartNew(async () => { await this.ServerLoop().ConfigureAwait(false); });

            if(!this.Config.OnlineMode)
                await this.Logger.LogMessageAsync($"Server is offline mode..");

            await Logger.LogMessageAsync($"Start listening for new clients");
            _tcpListener.Start();

            while (!_cts.IsCancellationRequested)
            {
                var tcp = await _tcpListener.AcceptTcpClientAsync();

                await Logger.LogMessageAsync($"New connection from client with IP {tcp.Client.RemoteEndPoint.ToString()}"); // it hurts when IP

                int newplayerid = 0;
                if (Clients.Count > 0)
                    newplayerid = this.Clients.Max(x => x.PlayerId);

                var clnt = new Client(tcp, this.Config, newplayerid, this);
                Clients.Add(clnt);

                await Task.Factory.StartNew(async () => { await clnt.StartConnectionAsync().ConfigureAwait(false); });
            }
            // Cancellation has been requested
            await Logger.LogMessageAsync($"Cancellation has been requested. Stopping server...");
            // TODO: TRY TO GRACEFULLY SHUT DOWN THE SERVER WE DONT WANT ERRORS REEEEEEEEEEE
        }

        public void StopServer()
        {
            this._cts.Cancel();
        }
    }
}