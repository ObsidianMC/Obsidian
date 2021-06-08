using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Obsidian.Utilities;

namespace Obsidian.Services
{
    public class ConsoleService : BackgroundService
    {
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Server _server;

        public ConsoleService(IHostApplicationLifetime lifetime, Server server)
        {
            _lifetime = lifetime;
            _server = server;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var input = ConsoleIO.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input == "stop")
                {
                    _lifetime.StopApplication();
                }
                else
                {
                    await _server.ExecuteCommand(input);
                }
            }
        }

        public async Task Listen(IReadOnlyDictionary<int, Server> servers, CancellationToken cancellationToken = default)
        {
            var currentServer = servers.First().Value;

            while (!cancellationToken.IsCancellationRequested)
            {
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input == "stop")
                {
                    _lifetime.StopApplication();
                }
                else
                {
                    
                }

                /*
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
                        if (!servers.TryGetValue(serverId, out var server))
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
                        if (!servers.TryGetValue(serverId, out var server))
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
                    await currentServer.ExecuteCommand(input);
                    if (input == "stop")
                    {
                        // servers.Remove(currentServer.Id);
                        currentServer = servers.FirstOrDefault().Value;
                    }
                }
                */
            }
        }
    }
}
