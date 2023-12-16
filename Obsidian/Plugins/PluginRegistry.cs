using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using System.Reflection;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(IServer server) : IPluginRegistry
{
    private readonly Server server = (Server)server;

    private CommandHandler CommandHandler => this.server.CommandsHandler;

    public IPluginRegistry MapCommand(ContextDelegate<CommandContext> contextDelegate)
    {

        return this;
    }

    public IPluginRegistry MapCommands()
    {
        var asm = Assembly.GetExecutingAssembly();


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
        this.CommandHandler.AddArgumentParser(parser);

        return this;
    }
}
