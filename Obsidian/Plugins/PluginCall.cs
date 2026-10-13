using System.Threading;

namespace Obsidian.Plugins;

/// <summary>
/// A call into a plugin, such as running one of its event or payload handlers or commands, which keeps the plugin from
/// being released (its services disposed and its load context unloaded) until the call ends.
/// </summary>
/// <remarks>
/// <para>
/// Start one with <see cref="PluginContainer.TryEnter"/> and dispose it on the same async flow when the call ends.
/// </para>
/// <para>
/// The calls also tell whose code runs on the current flow (<see cref="CurrentPlugin"/>): a plugin's calls see its own
/// <see cref="API.IServer"/> where shared objects hand out the server, and the entities they spawn are its own. The
/// server's handlers and commands run inside a plugin's call, like an event a plugin's handler raises, start a host
/// call (<see cref="EnterHost"/>), so that they see the server's own instances.
/// </para>
/// </remarks>
internal sealed class PluginCall : IDisposable
{
    // The innermost call the current async flow is in. A task started during a call inherits it, but sees it end, so
    // it doesn't count as being inside the call afterwards.
    private static readonly AsyncLocal<PluginCall?> current = new();

    private readonly PluginCall? outer;

    // Whether this is the server's code running inside a plugin's call, rather than a call into a plugin.
    private readonly bool host;

    private int ended;

    // Dropped when the call ends: a task started during the call keeps the chain of calls for as long as it runs, which
    // mustn't keep the plugin loaded.
    private PluginContainer? plugin;

    private PluginCall(PluginContainer? plugin, PluginCall? outer, bool host)
    {
        this.plugin = plugin;
        this.outer = outer;
        this.host = host;
    }

    /// <summary>
    /// Whether the current async flow is inside a call into any plugin. Such a flow can't wait for plugins to be
    /// released: it may be the call they wait for.
    /// </summary>
    public static bool InsideAny
    {
        get
        {
            for (var call = current.Value; call is not null; call = call.outer)
            {
                if (!call.host && Volatile.Read(ref call.ended) == 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The plugin whose call the current async flow is in, innermost first; null outside any, and in the server's
    /// handlers and commands run inside one (see <see cref="EnterHost"/>).
    /// </summary>
    public static PluginContainer? CurrentPlugin
    {
        get
        {
            for (var call = current.Value; call is not null; call = call.outer)
            {
                if (Volatile.Read(ref call.ended) != 0)
                    continue;

                return call.host ? null : Volatile.Read(ref call.plugin);
            }

            return null;
        }
    }

    // Called by PluginContainer.TryEnter once it counted the call. Setting the async local here, in a synchronous
    // method, sets it for the caller's flow.
    internal static PluginCall Start(PluginContainer plugin) => Push(plugin, host: false);

    /// <summary>
    /// Starts running the server's own handler or command on the current flow, which, inside a plugin's call, is no
    /// longer the plugin's code (see <see cref="CurrentPlugin"/>).
    /// </summary>
    /// <returns>The host call, to dispose when the handler ends, or null outside any plugin's call.</returns>
    internal static PluginCall? EnterHost() => InsideAny ? Push(null, host: true) : null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.ended, 1) == 1)
            return;

        if (current.Value == this)
            current.Value = this.outer;

        Interlocked.Exchange(ref this.plugin, null)?.Exit();
    }

    private static PluginCall Push(PluginContainer? plugin, bool host)
    {
        var call = new PluginCall(plugin, current.Value, host);
        current.Value = call;

        return call;
    }
}
