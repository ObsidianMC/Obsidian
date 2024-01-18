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

    public PluginContainer PluginContainer => this.pluginManager.GetPluginContainerByAssembly();

    public IPluginRegistry MapCommand(string name, Delegate handler)
    {
        this.commandHandler.RegisterCommand(this.PluginContainer, name, handler);
        return this;
    }

    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate)
    {
        this.commandHandler.RegisterCommand(this.PluginContainer, name, contextDelegate);
        return this;
    }
    
    public IPluginRegistry MapCommands()
    {
        this.commandHandler.RegisterCommands(this.PluginContainer);

        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {
        this.eventDispatcher.RegisterEvent(this.PluginContainer, contextDelegate, priority);

        return this;
    }

    public IPluginRegistry MapEvent(Delegate handler, Priority priority = Priority.Low) 
    {
        this.eventDispatcher.RegisterEvent(this.PluginContainer, handler, priority);

        return this;
    }

    public IPluginRegistry MapEvents()
    {
        this.eventDispatcher.RegisterEvents(this.PluginContainer);

        return this;
    }

    public IPluginRegistry RegisterCommandArgumentHandler<T>(T parser) where T : BaseArgumentParser
    {
        this.commandHandler.AddArgumentParser(parser);

        return this;
    }
}
