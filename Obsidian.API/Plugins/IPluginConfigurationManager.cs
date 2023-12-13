using Obsidian.API.Events;

namespace Obsidian.API.Plugins;
public interface IPluginConfigurationManager
{
    public IPluginConfigurationManager MapCommands();
    public IPluginConfigurationManager MapCommand(ContextDelegate<CommandContext> contextDelegate);

    public IPluginConfigurationManager MapEvent<TEventArgs>(ContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs;
    public IPluginConfigurationManager MapEvents();
}


//TODO better name maybe??
public delegate ValueTask ContextDelegate<TContext>(TContext context);
