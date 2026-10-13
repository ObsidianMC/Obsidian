namespace Obsidian.API;

/// <summary>
/// A server that shows each plugin its own <see cref="IServer"/> while the plugin's code runs, so that what a plugin
/// adds through the server it's handed by shared objects (event arguments, command and payload contexts, connections)
/// is the plugin's, and unloading it removes that.
/// </summary>
internal interface IPluginScopedServer
{
    /// <summary>
    /// The server as the code running now sees it: during a call into a plugin, such as one of its event handlers or
    /// commands, the plugin's own instance; otherwise the server itself.
    /// </summary>
    public IServer ForCurrentCall();
}

/// <summary>
/// Resolves the server shared objects hand out (see <see cref="IPluginScopedServer"/>).
/// </summary>
internal static class ServerScope
{
    /// <summary>
    /// What <paramref name="server"/> is to the code running now: a plugin's own instance during the plugin's calls.
    /// </summary>
    public static IServer Resolve(IServer server) =>
        server is IPluginScopedServer scoped ? scoped.ForCurrentCall() : server;
}
