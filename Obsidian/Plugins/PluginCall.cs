using System.Threading;

namespace Obsidian.Plugins;

/// <summary>
/// A call into a plugin, such as running one of its event or payload handlers or commands, which keeps the plugin from
/// being released (its services disposed and its load context unloaded) until the call ends.
/// </summary>
/// <remarks>
/// Start one with <see cref="PluginContainer.TryEnter"/> and dispose it on the same async flow when the call ends.
/// </remarks>
internal sealed class PluginCall : IDisposable
{
    // The innermost call the current async flow is in. A task started during a call inherits it, but sees it end, so
    // it doesn't count as being inside the call afterwards.
    private static readonly AsyncLocal<PluginCall?> current = new();

    private readonly PluginCall? outer;
    private int ended;

    // Dropped when the call ends: a task started during the call keeps the chain of calls for as long as it runs, which
    // mustn't keep the plugin loaded.
    private PluginContainer? plugin;

    private PluginCall(PluginContainer plugin, PluginCall? outer)
    {
        this.plugin = plugin;
        this.outer = outer;
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
                if (Volatile.Read(ref call.ended) == 0)
                    return true;
            }

            return false;
        }
    }

    // Called by PluginContainer.TryEnter once it counted the call. Setting the async local here, in a synchronous
    // method, sets it for the caller's flow.
    internal static PluginCall Start(PluginContainer plugin)
    {
        var call = new PluginCall(plugin, current.Value);
        current.Value = call;

        return call;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.ended, 1) == 1)
            return;

        if (current.Value == this)
            current.Value = this.outer;

        Interlocked.Exchange(ref this.plugin, null)?.Exit();
    }
}
