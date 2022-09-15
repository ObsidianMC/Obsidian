using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IServerSetup _setup;

    public ObsidianHostingService(IServerSetup setup)
    {
        _setup = setup;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        var config = await _setup.LoadServerConfiguration(cToken);
        var worlds = await _setup.LoadServerWorlds(cToken);

        var server = new Server(config, worlds);

    }

}
