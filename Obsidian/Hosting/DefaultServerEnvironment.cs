﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.IO;
using System.Threading;

namespace Obsidian.Hosting;

/// <summary>
/// A default <see cref="IServerEnvironment"/> implementation aimed for Console applications.
/// Loads the server configuration and worlds using the current working directory and
/// forwards commands from the standard input to the server.
/// 
/// Use the <see cref="CreateAsync"/> method to create an instance.
/// </summary>
public sealed class DefaultServerEnvironment : IServerEnvironment, IDisposable
{
    private readonly ILogger<DefaultServerEnvironment> logger;

    public IOptionsMonitor<IServerConfiguration> ServerConfig { get; }
    public List<ServerWorld> ServerWorlds { get; } = default!;

    internal DefaultServerEnvironment(IOptionsMonitor<IServerConfiguration> serverConfig, ILogger<DefaultServerEnvironment> logger)
    {
        ServerConfig = serverConfig;
        this.logger = logger;
    }

    /// <summary>
    /// Provide server commands using the Console.
    /// </summary>
    /// <param name="server"></param>
    /// <param name="cToken"></param>
    /// <returns></returns>
    public async Task ProvideServerCommandsAsync(Server server, CancellationToken cToken)
    {
        while (!cToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            if (input == null) continue;
            await server.ExecuteCommand(input);
        }
    }

    Task IServerEnvironment.OnServerStoppedGracefullyAsync()
    {
        logger.LogInformation("Goodbye!");
        return Task.CompletedTask;
    }
    Task IServerEnvironment.OnServerCrashAsync(Exception e)
    {
        // Write crash log somewhere?
        var byeMessages = new[]
        {
            "We had a good run...",
            "At least we tried...",
            "Who could've seen this one coming...",
            "Try turning it off and on again...",
            "I blame Naamloos for this one...",
            "I blame Sebastian for this one...",
            "I blame Tides for this one...",
            "I blame Craftplacer for this one..."
        };

        logger.LogCritical("Obsidian has crashed!");
        logger.LogCritical("{message}", byeMessages[new Random().Next(byeMessages.Length)]);
        logger.LogCritical(e, "Reason: {reason}", e.Message);
        logger.LogCritical("{}", e.StackTrace);
        return Task.CompletedTask;
    }

    private static async Task<List<ServerWorld>> LoadServerWorldsAsync()
    {
        var worldsFile = new FileInfo(Path.Combine("config", "worlds.json"));

        if (worldsFile.Exists)
        {
            await using var worldsFileStream = worldsFile.OpenRead();
            return await worldsFileStream.FromJsonAsync<List<ServerWorld>>()
                ?? throw new Exception("A worlds file does exist, but is invalid. Is it corrupt?");
        }

        var worlds = new List<ServerWorld>()
            {
                new()
                {
                    ChildDimensions =
                    {
                        "minecraft:the_nether",
                        "minecraft:the_end"
                    }
                }
            };

        await using var fileStream = worldsFile.Create();
        await worlds.ToJsonAsync(fileStream);

        return worlds;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        
    }
}

