using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.Threading;

namespace Obsidian.Hosting;

/// <summary>
/// A default <see cref="IServerEnvironment"/> implementation aimed for Console applications.
/// Loads the server configuration and worlds using the current working directory and
/// forwards commands from the standard input to the server.
/// 
/// Use the <see cref="CreateAsync"/> method to create an instance.
/// </summary>
internal sealed class DefaultServerEnvironment(IOptionsMonitor<ServerConfiguration> serverConfig, IConfiguration configuration,
    ILogger<DefaultServerEnvironment> logger) : IServerEnvironment, IDisposable
{
    private readonly ILogger<DefaultServerEnvironment> logger = logger;

    public IOptionsMonitor<ServerConfiguration> ServerConfig { get; } = serverConfig;

    /// <summary>
    /// Provide server commands using the Console.
    /// </summary>
    /// <param name="server"></param>
    /// <param name="cToken"></param>
    /// <returns></returns>
    public async Task ProvideServerCommandsAsync(Server server, CancellationToken cToken)
    {
        if (configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
            return;

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
        // FileLogger implemented in ConsoleApp
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);

    }
}

