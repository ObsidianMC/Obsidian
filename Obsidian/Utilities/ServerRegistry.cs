using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Obsidian.Utilities
{
    public class ServerRegistry : IEnumerable<Server>
    {
        private readonly ILogger<ServerRegistry> _logger;

        private readonly Dictionary<int, Server> _serverList = new();
        private readonly HashSet<int> _ports = new();

        public int Count => _serverList.Count;

        public ServerRegistry(ILogger<ServerRegistry> logger)
        {
            _logger = logger;
        }

        public Server this[int index] => _serverList[index];

        public bool TryAddServer(Server server)
        {
            if (_ports.Contains(server.Port))
            {
                return false;
            }

            _serverList[server.Id] = server;
            _ports.Add(server.Port);
            return true;
        }

        public bool RemoveServer(int id, out Server value)
        {
            var removed = _serverList.Remove(id, out value);
            if (removed)
            {
                _ = _ports.Remove(value.Port);
            }
            return removed;
        } 

        public bool TryGetServer(int id, out Server server)
        {
            server = null;
            if (_serverList.ContainsKey(id))
            {
                server = _serverList[id];
                return true;
            }

            return false;
        }

        public async Task RegisterAllServersAsync(int serverCount, string version, CancellationToken stoppingToken = default)
        {
            for (int i = 0; i < serverCount; i++)
            {
                string serverDir = $"Server-{i}";

                Directory.CreateDirectory(serverDir);

                string configPath = Path.Combine(serverDir, "config.json");
                Config config;

                if (File.Exists(configPath))
                {
                    config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync(configPath, stoppingToken));
                }
                else
                {
                    config = new Config { Port = 25565 + i };
                    await File.WriteAllTextAsync(configPath, JsonConvert.SerializeObject(config, Formatting.Indented), stoppingToken);
                    Console.WriteLine($"Created new configuration file for Server-{i}");
                }

                if (!TryAddServer(new Server(config, version, i)))
                {
                    throw new InvalidOperationException("Multiple servers cannot be bound to the same port");
                }
            }
        }

        public IEnumerator<Server> GetEnumerator() => _serverList.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
