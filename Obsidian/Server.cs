using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Concurrency;
using Obsidian.Connection;
using Obsidian.Entities;
using Obsidian.Logging;

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

            await Logger.LogMessageAsync($"Start listening for new clients");
            _tcpListener.Start();

            while (!_cts.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync();
                // TODO check if this is correctly fetching IP
                await Logger.LogMessageAsync($"New connection from client with IP {client.Client.RemoteEndPoint.ToString()}"); // it hurts when IP
                var clnt = new Client(client, Logger, this.Config);
                _clients.Add(clnt);
                await Task.Run(clnt.StartClientConnection);
                // Now just to do connection stuff
            }
            // Cancellation has been requested
            await Logger.LogMessageAsync($"Cancellation has been requested. Stopping server...");
            // TRY TO GRACEFULLY SHUT DOWN THE SERVER WE DONT WANT ERRORS REEEEEEEEEEE
        }

        public void StopServer()
        {
            this._cts.Cancel();
        }
    }
}
