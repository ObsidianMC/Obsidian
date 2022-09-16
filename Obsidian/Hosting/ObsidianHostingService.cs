using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServerEnvironment _setup;
    private readonly Server _server;

    public ObsidianHostingService(IHostApplicationLifetime lifetime, Server server)
    {
        _server = server;
        _lifetime = lifetime;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        await _server.RunAsync();

        if (_setup.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }


}
