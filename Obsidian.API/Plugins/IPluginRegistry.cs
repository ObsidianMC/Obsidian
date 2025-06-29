using Obsidian.API.Events;

namespace Obsidian.API.Plugins;
public interface IPluginRegistry
{
    public IPluginRegistry RegisterCommandArgumentHandler<T>(BaseArgumentParser<T> parser);

    public IPluginRegistry MapCommands();
    public IPluginRegistry MapCommand(string name, Delegate contextDelegate);
    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate);

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs;
    public IPluginRegistry MapEvent(Delegate contextDelegate, Priority priority = Priority.Low);
    public IPluginRegistry MapEvents();
}


//TODO better name maybe??
public delegate ValueTask ValueTaskContextDelegate<TContext>(TContext context);
