using Obsidian.API.Events;
using Obsidian.API.Plugins;

namespace Obsidian.API;
public interface IEventDispatcher : IDisposable
{
    public ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs;

    public void RegisterEvents(IPluginContainer? pluginContainer = null);
}
