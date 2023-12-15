using Obsidian.API.Events;
using Obsidian.API.Plugins;

namespace Obsidian.Plugins;
public sealed class PluginRegistry : IPluginRegistry
{

    public IPluginRegistry MapCommand(ContextDelegate<CommandContext> contextDelegate)
    {

        return this;
    }

    public IPluginRegistry MapCommands()
    {

        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {

        return this;
    }

    public IPluginRegistry MapEvents()
    {

        return this;
    }
}
