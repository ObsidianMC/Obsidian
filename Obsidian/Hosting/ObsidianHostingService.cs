using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService(
    IHostApplicationLifetime lifetime,
    IServer server,
    IServerEnvironment env,
    IOptionsMonitor<ServerConfiguration> serverConfiguration) : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly IServerEnvironment _environment = env;
    private readonly IServer _server = server;
    private readonly IOptionsMonitor<ServerConfiguration> serverConfiguration = serverConfiguration;

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        try
        {
            await _server.RunAsync();
            await _environment.OnServerStoppedGracefullyAsync();
        }
        catch (Exception e)
        {
            await _environment.OnServerCrashAsync(e);
        }

        if (serverConfiguration.CurrentValue.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }

}
