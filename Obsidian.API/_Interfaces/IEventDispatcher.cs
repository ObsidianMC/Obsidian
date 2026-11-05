using Obsidian.API.Events;
using Obsidian.API.Plugins;

namespace Obsidian.API;

/// <summary>Registers and dispatches Minecraft event handlers.</summary>
/// <remarks>
/// Handlers can run concurrently on different threads. Events raised during a level's tick run as part of that
/// level's tick, while other levels may tick concurrently. Packet-driven events run on the connection's processing
/// thread. Asynchronous continuations may resume on another thread; there is no fixed thread affinity.
/// Handlers must make state shared across events thread-safe.
/// </remarks>
public interface IEventDispatcher : IDisposable
{
    /// <summary>Runs the registered handlers in the calling context, awaiting each in priority order.</summary>
    /// <remarks>Other dispatches may run concurrently; see <see cref="IEventDispatcher"/>.</remarks>
    public ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs;

    /// <summary>Registers attributed handlers from <see cref="MinecraftEventHandler"/> modules.</summary>
    /// <remarks>Registered handlers follow the threading contract of <see cref="IEventDispatcher"/>.</remarks>
    public void RegisterEvents(IPluginContainer? pluginContainer = null);
}
