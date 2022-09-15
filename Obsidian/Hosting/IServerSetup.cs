using System.Threading;

namespace Obsidian.Hosting;
public interface IServerSetup
{
    /// <summary>
    /// If set to true, after the server shuts down, the application will stop running as well.
    /// </summary>
    public bool ServerShutdownStopsProgram { get; }
    /// <summary>
    /// Load the server configuration.
    /// </summary>
    /// <param name="cToken"></param>
    /// <returns></returns>
    Task<IServerConfiguration> LoadServerConfiguration(CancellationToken cToken);
    /// <summary>
    /// Load the server worlds.
    /// </summary>
    /// <param name="cToken"></param>
    /// <returns></returns>
    Task<List<ServerWorld>> LoadServerWorlds(CancellationToken cToken);
    /// <summary>
    /// Execute commands on the server. This task will run for the lifetime of the server.
    /// </summary>
    /// <param name="cToken"></param>
    /// <returns></returns>
    Task ProvideServerCommands(Server server, CancellationToken cToken);
}
