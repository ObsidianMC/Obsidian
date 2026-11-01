using Microsoft.Extensions.Logging;

namespace Obsidian.Hosting;

/// <summary>
/// Interface that has to be implemented by the project that hosts Obsidian using a generic host.
/// An instance of the class that implements this interface has to be passed while adding obsidian as a service to the DI container.
/// You can use a pre-made environment, <seealso cref="DefaultServerEnvironment"/>.
/// </summary>
public interface IServerEnvironment
{
    /// <summary>
    /// Called when the server succesfuly ran to completion.
    /// </summary>
    /// <param name="logger"></param>
    /// <returns></returns>
    public ValueTask OnServerStoppedGracefullyAsync();

    /// <summary>
    /// Called when the server stopped due to a crash.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="e"></param>
    /// <returns></returns>
    public ValueTask OnServerCrashAsync(Exception e);

    /// <summary>
    /// Called when a world can't be loaded for a reason the server's operator can act on, such as a save the world
    /// upgrade doesn't support: <paramref name="message"/> says why. The server should stop. By default, it's reported
    /// like a crash.
    /// </summary>
    public ValueTask OnWorldLoadFailedAsync(string message) =>
        this.OnServerCrashAsync(new InvalidOperationException(message));

}
