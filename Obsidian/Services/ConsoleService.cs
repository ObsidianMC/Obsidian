using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Obsidian.Utilities;

namespace Obsidian.Services
{
    public class ConsoleService
    {
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ServerRegistry _serverRegistry;

        public ConsoleService(IHostApplicationLifetime lifetime, ServerRegistry serverRegistry)
        {
            _lifetime = lifetime;
            _serverRegistry = serverRegistry;
        }

        public async Task Listen(CancellationToken stoppingToken = default)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var input = ConsoleIO.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                Server currentServer = _serverRegistry[0];

                if (input.StartsWith('.'))
                {
                    if (input.StartsWith(".switch"))
                    {
                        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                        {
                            ConsoleIO.WriteLine("Invalid server id");
                            continue;
                        }
                        if (!int.TryParse(parts[1], out int serverId))
                        {
                            ConsoleIO.WriteLine("Invalid server id");
                            continue;
                        }
                        if (!_serverRegistry.TryGetServer(serverId, out var server))
                        {
                            ConsoleIO.WriteLine("No server with given id found");
                            continue;
                        }

                        currentServer = server;
                        ConsoleIO.WriteLine($"Changed current server to {server.Id}");
                    }
                    else if (input.StartsWith(".execute"))
                    {
                        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3)
                        {
                            ConsoleIO.WriteLine("Invalid server id or command");
                            continue;
                        }
                        if (!int.TryParse(parts[1], out int serverId))
                        {
                            ConsoleIO.WriteLine("Invalid server id");
                            continue;
                        }
                        if (!_serverRegistry.TryGetServer(serverId, out var server))
                        {
                            ConsoleIO.WriteLine("No server with given id found");
                            continue;
                        }

                        ConsoleIO.WriteLine($"Executing command on Server-{server.Id}");
                        await server.ExecuteCommand(string.Join(' ', parts.Skip(2)));
                    }
                }
                else
                {
                    await currentServer!.ExecuteCommand(input);
                    if (input == "stop")
                    {
                        _serverRegistry.RemoveServer(currentServer.Id, out _);

                        if (!_serverRegistry.Any())
                        {
                            // If there are no servers running, stop.
                            _lifetime.StopApplication();
                            break;
                        }
                        currentServer = _serverRegistry.FirstOrDefault();
                    }
                }
            }
        }
    }
}
