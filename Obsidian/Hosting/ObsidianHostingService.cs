using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServerEnvironment _env;
    private readonly Server _server;
    private readonly ILogger _logger;

    public ObsidianHostingService(
        IHostApplicationLifetime lifetime, 
        Server server, 
        IServerEnvironment env,
        ILogger<ObsidianHostingService> logger)
    {
        _server = server;
        _lifetime = lifetime;
        _env = env;
        _logger = logger;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        try
        {
            await _server.RunAsync();
            _logger.LogInformation("Goodbye :)");
        }
        catch (Exception e)
        {
            _logger.LogCritical("Obsidian has crashed...");
            _logger.LogCritical(e, "Reason: {reason}", e.Message);
            _logger.LogCritical("{}", e.StackTrace);
        }

        if (_env.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }
}
