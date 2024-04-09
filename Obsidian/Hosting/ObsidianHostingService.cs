using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService(
    IHostApplicationLifetime lifetime,
    IServer server,
    IServerEnvironment env,
    ILogger<ObsidianHostingService> logger,
    IOptionsMonitor<IServerConfiguration> serverConfiguration) : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly IServerEnvironment _environment = env;
    private readonly IServer _server = server;
    private readonly ILogger _logger = logger;
    private readonly IOptionsMonitor<IServerConfiguration> serverConfiguration = serverConfiguration;

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

        if (serverConfiguration.CurrentValue.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }

}
