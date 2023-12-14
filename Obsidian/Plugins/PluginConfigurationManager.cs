using Obsidian.API.Events;
using Obsidian.Plugins;

namespace Obsidian.API.Plugins;
public sealed class PluginConfigurationManager(PluginManager pluginManager) :  IPluginRegistry
{
    private readonly PluginManager pluginManager = pluginManager;

    public IPluginRegistry MapCommands()
    {
        
        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {

        return this;
    }

    public IPluginRegistry MapCommand(ContextDelegate<CommandContext> contextDelegate)
    {


        return this;
    }

    public IPluginRegistry MapEvents()
    {

        return this;
    }
}
