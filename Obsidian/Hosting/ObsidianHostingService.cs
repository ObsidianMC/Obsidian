using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Obsidian.Hosting;
internal sealed class ObsidianHostingService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServerSetup _setup;

    public ObsidianHostingService(IHostApplicationLifetime lifetime, IServerSetup setup)
    {
        _setup = setup;
        _lifetime = lifetime;
    }

    protected async override Task ExecuteAsync(CancellationToken cToken)
    {
        var config = await _setup.LoadServerConfiguration(cToken);
        var worlds = await _setup.LoadServerWorlds(cToken);
        var server = new Server(config, worlds);

        _ = Task.Run(() => _setup.ProvideServerCommands(server, cToken), cToken);
        await server.RunAsync();

        if (_setup.ServerShutdownStopsProgram)
            _lifetime.StopApplication();
    }


}
