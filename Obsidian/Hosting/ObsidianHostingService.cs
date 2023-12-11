using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServerEnvironment _environment;
    private readonly IServer _server;
    private readonly ILogger _logger;

    public ObsidianHostingService(
        IHostApplicationLifetime lifetime,
        IServer server,
        IServerEnvironment env,
        ILogger<ObsidianHostingService> logger)
    {
        _server = server;
        _lifetime = lifetime;
        _environment = env;
        _logger = logger;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        try
        {
            await _server.RunAsync();
            await _environment.OnServerStoppedGracefullyAsync(_logger);
        }
        catch (Exception e)
        {
            await _environment.OnServerCrashAsync(_logger, e);
        }

        if (_environment.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }

}
