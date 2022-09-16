using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServerEnvironment _env;
    private readonly Server _server;

    public ObsidianHostingService(IHostApplicationLifetime lifetime, Server server, IServerEnvironment env)
    {
        _server = server;
        _lifetime = lifetime;
        _env = env;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        await _server.RunAsync();

        if (_env.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }
}
