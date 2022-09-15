using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IServerSetup _setup;
    private Server? _server;

    public ObsidianHostingService(IServerSetup setup)
    {
        _setup = setup;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        var config = await _setup.LoadServerConfiguration(cToken);
        var worlds = await _setup.LoadServerWorlds(cToken);

        _server = new Server(config, worlds);
        await _server.RunAsync();
    }
}
