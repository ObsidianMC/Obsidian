using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Hosting;

namespace Obsidian.Integrated;

/// <summary>
/// The <see cref="IServerEnvironment"/> of an integrated server: a crash is reported to the client as an event, and the
/// process then stops with exit code 1.
/// </summary>
internal sealed partial class IntegratedServerEnvironment(
    IntegratedEventWriter events,
    IHostApplicationLifetime lifetime,
    ILogger<IntegratedServerEnvironment> logger) : IServerEnvironment
{
    public ValueTask OnServerStoppedGracefullyAsync() => default;

    public ValueTask OnServerCrashAsync(Exception e)
    {
        Log.Crashed(logger, e);
        events.Write(IntegratedEvent.Crashed(e.Message));

        Environment.ExitCode = 1;
        lifetime.StopApplication();

        return default;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Critical, Message = "The integrated server crashed")]
        public static partial void Crashed(ILogger logger, Exception exception);
    }
}
