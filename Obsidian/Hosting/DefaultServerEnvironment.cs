using Microsoft.Extensions.Logging;

namespace Obsidian.Hosting;

/// <summary>
/// A default <see cref="IServerEnvironment"/> implementation aimed for Console applications.
/// Console commands are read by <see cref="Console.ConsoleCommandService"/>.
/// </summary>
internal sealed partial class DefaultServerEnvironment(ILogger<DefaultServerEnvironment> logger) : IServerEnvironment
{
    private readonly ILogger<DefaultServerEnvironment> logger = logger;

    // The server logs its own shutdown.
    public ValueTask OnServerStoppedGracefullyAsync() => default;

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
            "I blame Craftplacer for this one...",
            "This is really just your fault",
            "Please tell me you have a backup of your world teehee",
            "This is all Mojang's fault, not mine",
        };

        var byeMessage = byeMessages[Random.Shared.Next(byeMessages.Length)];
        Log.Crashed(this.logger, e, byeMessage);
        return default;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Critical, Message = "Obsidian has crashed! {ByeMessage}")]
        public static partial void Crashed(ILogger logger, Exception exception, string byeMessage);
    }
}

