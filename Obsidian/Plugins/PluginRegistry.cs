using Obsidian.API.Events;
using Obsidian.API.Plugins;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(IServer server) : IPluginRegistry
{
    private readonly Server server = (Server)server;

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

    public IPluginRegistry RegisterArgumentHandler<T>(T parser) where T : BaseArgumentParser
    {
        this.server.CommandsHandler.AddArgumentParser(parser);

        return this;
    }
}
