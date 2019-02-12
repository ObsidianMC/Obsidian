using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Concurrency;
using Obsidian.Connection;
using Obsidian.Entities;
using Obsidian.Logging;
using Obsidian.Packets;
using Obsidian.Commands;
using Qmmands;
using System.Reflection;

namespace Obsidian
{
    public class Server
    {
        public Logger Logger { get; private set; }
        public int Port { get; private set; }
        public string Version { get; private set; }
        public string Id { get; private set; }
        public Config Config { get; private set; }

        private CancellationTokenSource _cts;
        private TcpListener _tcpListener;
        private ConcurrentHashSet<Client> _clients;

        /// <summary>
        /// Creates a new Server instance. Spawning multiple of these could make a multi-server setup  :thinking:
        /// </summary>
        /// <param name="version">Version the server is running.</param>
        public Server(Config config, string version, string serverid)
        {
            this.Logger = new Logger($"Obsidian ID: {serverid}");
            this.Port = config.Port;
            this.Version = version;
            this.Id = serverid;
            this._tcpListener = new TcpListener(IPAddress.Any, this.Port);
            this._clients = new ConcurrentHashSet<Client>();
            this._cts = new CancellationTokenSource();
            this.Config = config;
        }

        /// <summary>
        /// Starts this server
        /// </summary>
        /// <returns></returns>
        public async Task StartServer()
        {
            await Logger.LogMessageAsync($"Launching Obsidian Server v {Version} with ID {Id}");

            await Logger.LogMessageAsync("Starting server backend...");
            Task.Run(async () => { await this.ServerLoop(); });

            await Logger.LogMessageAsync($"Start listening for new clients");
            _tcpListener.Start();

            while (!_cts.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync();
                // TODO check if this is correctly fetching IP
                await Logger.LogMessageAsync($"New connection from client with IP {client.Client.RemoteEndPoint.ToString()}"); // it hurts when IP
                var clnt = new Client(client, Logger, this.Config, this);
                _clients.Add(clnt);
                // Don't await, it will run parralel from all other clients.
                Task.Run(async () => { await clnt.StartClientConnection().ConfigureAwait(false); });
            }
            // Cancellation has been requested
            await Logger.LogMessageAsync($"Cancellation has been requested. Stopping server...");
            // TRY TO GRACEFULLY SHUT DOWN THE SERVER WE DONT WANT ERRORS REEEEEEEEEEE
        }

        ConcurrentHashSet<List<QueueChat>> ChatMessages;
        CommandService _cmd;
        int keepaliveticks = 0;
        long keepaliveid = 0;
        private async Task ServerLoop()
        {
            ChatMessages = new ConcurrentHashSet<List<QueueChat>>();
            _cmd = new CommandService(new CommandServiceConfiguration()
            {
                CaseSensitive = false,
                DefaultRunMode = RunMode.Parallel,
                IgnoreExtraArguments = true
            });
            _cmd.AddModule<MainCommandModule>();

            while (!_cts.IsCancellationRequested)
            {
                // Loop shit
                await Task.Delay(50);

                keepaliveticks++;
                if(keepaliveticks > 200)
                {
                    keepaliveid = DateTime.Now.Ticks;
                    await Logger.LogMessageAsync($"Broadcasting keepalive {keepaliveid}");
                    foreach(var clnt in this._clients)
                    {
                        if (clnt.State == PacketState.Play)
                        {
                            await clnt.SendKeepAliveAsync(keepaliveid);
                        }
                    }
                    keepaliveticks = 0;
                }

                // Chat
                if(ChatMessages.Count > 0)
                {
                    var msg = ChatMessages.First();
                    foreach(var clnt in this._clients)
                    {
                        if (clnt.State == PacketState.Play)
                        {
                            foreach (var m in msg)
                            {
                                await clnt.SendChatAsync(m.Message, m.Position);
                            }
                        }
                    }
                    ChatMessages.TryRemove(msg);
                }

                foreach(var client in _clients)
                {
                    if(client.KeepAlives > 5)
                    {
                        client.DisconnectClient();
                    }
                }
            }
        }

        public async Task SendChatAsync(string message, Client source, byte position = 0, bool system = false)
        {
            // if author is null that means chat is sent by system.
            if (system)
            {
                ChatMessages.Add(new List<QueueChat> { new QueueChat() { Message = message, Position = position } });
                await Logger.LogMessageAsync(message);
            }
            else
            {
                if (!CommandUtilities.HasPrefix(message, '/', out string output))
                {
                    string formattedmsg = $"<{source.Player.Username}> {message}";
                    ChatMessages.Add(new List<QueueChat> { new QueueChat() { Message = formattedmsg, Position = position } });
                    await Logger.LogMessageAsync(formattedmsg);
                    return;
                }

                var context = new CommandContext(source, this);
                IResult result = await _cmd.ExecuteAsync(output, context);
                if (!result.IsSuccessful)
                    ChatMessages.Add(new List<QueueChat> { new QueueChat() { Message = $"{MinecraftColor.Red}Command error: {(result as FailedResult).Reason}",
                        Position = position } });
            }
        }

        public void StopServer()
        {
            this._cts.Cancel();
        }
    }

    public struct QueueChat
    {
        public string Message;
        public byte Position;
    }
}
