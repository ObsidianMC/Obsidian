using Microsoft.Extensions.Logging;

namespace Obsidian.Hosting;

/// <summary>
/// A default <see cref="IServerEnvironment"/> implementation aimed for Console applications.
/// Console commands are read by <see cref="Console.ConsoleCommandService"/>.
/// </summary>
internal sealed class DefaultServerEnvironment(ILogger<DefaultServerEnvironment> logger) : IServerEnvironment
{
    private readonly ILogger<DefaultServerEnvironment> logger = logger;

    public ValueTask OnServerStoppedGracefullyAsync()
    {
        logger.LogInformation("Goodbye!");
        return default;
    }

    public ValueTask OnServerCrashAsync(Exception e)
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
        return default;
    }
}

