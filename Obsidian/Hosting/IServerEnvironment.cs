using Microsoft.Extensions.Logging;
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
    public Task ProvideServerCommandsAsync(Server server, CancellationToken cToken);

    /// <summary>
    /// Called when the server succesfuly ran to completion.
    /// </summary>
    /// <param name="logger"></param>
    /// <returns></returns>
    public Task OnServerStoppedGracefullyAsync(ILogger logger);

    /// <summary>
    /// Called when the server stopped due to a crash.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="e"></param>
    /// <returns></returns>
    public Task OnServerCrashAsync(ILogger logger, Exception e);

    /// <summary>
    /// Create a <see cref="DefaultServerEnvironment"/> asynchronously, which is aimed for use in Console Applications.
    /// </summary>
    /// <returns></returns>
    public static Task<DefaultServerEnvironment> CreateDefaultAsync() => DefaultServerEnvironment.CreateAsync();

}
