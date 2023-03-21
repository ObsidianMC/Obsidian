using Obsidian;
using Obsidian.Hosting;
using Obsidian.Utilities;

namespace ObsidianCloud.ClientService;

public class CloudServerEnvironment : IServerEnvironment
{
    public bool ServerShutdownStopsProgram { get; } = false;

    public ServerConfiguration Configuration => throw new NotImplementedException();

    public List<ServerWorld> ServerWorlds => throw new NotImplementedException();

    public Task OnServerCrashAsync(ILogger logger, Exception e) => throw new NotImplementedException();
    public Task OnServerStoppedGracefullyAsync(ILogger logger) => throw new NotImplementedException();
    public Task ProvideServerCommandsAsync(Server server, CancellationToken cToken) => throw new NotImplementedException();

}
