using Obsidian.API.Events;

namespace Obsidian.API.Plugins;
public interface IPluginRegistry
{
    public IPluginRegistry MapCommands();
    public IPluginRegistry MapCommand(ContextDelegate<CommandContext> contextDelegate);

    public IPluginRegistry MapEvent<TEventArgs>(ContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs;
    public IPluginRegistry MapEvents();
}


//TODO better name maybe??
public delegate ValueTask ContextDelegate<TContext>(TContext context);
