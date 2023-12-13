using Obsidian.API.Events;
using Obsidian.Plugins;

namespace Obsidian.API.Plugins;
public sealed class PluginConfigurationManager(PluginManager pluginManager) :  IPluginConfigurationManager
{
    private readonly PluginManager pluginManager = pluginManager;

    public IPluginConfigurationManager MapCommands()
    {
        
        return this;
    }

    public IPluginConfigurationManager MapEvent<TEventArgs>(ContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {

        return this;
    }

    public IPluginConfigurationManager MapCommand(ContextDelegate<CommandContext> contextDelegate)
    {


        return this;
    }

    public IPluginConfigurationManager MapEvents()
    {

        return this;
    }
}
