using Obsidian.API.Events;

namespace Obsidian.API;
public interface IEventDispatcher : IDisposable
{
    public ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs;
}
