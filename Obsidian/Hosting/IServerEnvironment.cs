using System.Threading;

namespace Obsidian.Hosting;

/// <summary>
/// Interface that has to be implemented by the project that hosts Obsidian using a generic host.
/// An instance of the class that implements this interface has to be passed while adding obsidian as a service to the DI container.
/// You can use a pre-made environment, <seealso cref="DefaultServerEnvironment"/>.
/// </summary>
public interface IServerEnvironment
{
    /// <summary>
    /// If set to true, after the server shuts down, the application will stop running as well.
    /// </summary>
    public bool ServerShutdownStopsProgram { get; }
    public ServerConfiguration Configuration { get; }
    public List<ServerWorld> ServerWorlds { get; }

    /// <summary>
    /// Execute commands on the server. This task will run for the lifetime of the server.
    /// </summary>
    /// <param name="cToken"></param>
    /// <returns></returns>
    Task ProvideServerCommands(Server server, CancellationToken cToken);
}
