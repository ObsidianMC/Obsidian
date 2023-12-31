using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Services;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(PluginManager pluginManager, EventDispatcher eventDispatcher, CommandHandler commandHandler, ILogger logger) : IPluginRegistry
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly EventDispatcher eventDispatcher = eventDispatcher;
    private readonly CommandHandler commandHandler = commandHandler;
    private readonly ILogger logger = logger;

    //TODO REGISTER DELEGATES
    public IPluginRegistry MapCommand(string name, Delegate handler)
    {

        return this;
    }

    //TODO REGISTER DELEGATES
    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate)
    {

        return this;
    }
    
    public IPluginRegistry MapCommands()
    {
        this.commandHandler.RegisterCommands(this.pluginManager.GetPluginContainerByAssembly());

        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {
        var pluginContainer = this.pluginManager.GetPluginContainerByAssembly();

        this.eventDispatcher.RegisterEvent(pluginContainer, contextDelegate, priority);

        return this;
    }

    public IPluginRegistry MapEvent(Delegate handler, Priority priority = Priority.Low) 
    {
        var pluginContainer = this.pluginManager.GetPluginContainerByAssembly();

        this.eventDispatcher.RegisterEvent(pluginContainer, handler, priority);

        return this;
    }

    public IPluginRegistry MapEvents()
    {
        var pluginContainer = this.pluginManager.GetPluginContainerByAssembly();

        this.eventDispatcher.RegisterEvents(pluginContainer);

        return this;
    }

    public IPluginRegistry RegisterCommandArgumentHandler<T>(T parser) where T : BaseArgumentParser
    {
        this.commandHandler.AddArgumentParser(parser);

        return this;
    }
}
