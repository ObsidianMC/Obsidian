using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Obsidian.Utilities;

namespace Obsidian
{
    public sealed class ObsidianServer : BackgroundService
    {
        private readonly GlobalConfig _config;
        
        // Will implement multi-server functionality later
        // private readonly Dictionary<int, Server> servers = new();
        private Server _server;

        public ObsidianServer(IOptions<GlobalConfig> config)
        {
            _config = config.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string serverDir = "Server";
            Directory.CreateDirectory(serverDir);

            string configPath = Path.Combine(serverDir, "config.json");
            Config config;

            if (File.Exists(configPath))
            {
                config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync(configPath, stoppingToken));
            }
            else
            {
                config = new Config();
                await File.WriteAllTextAsync(configPath, JsonConvert.SerializeObject(config, Formatting.Indented), stoppingToken);
                Console.WriteLine($"Created new configuration file for Server");
            }

            _server = new Server(config, version, 1);
        }
    }
}
