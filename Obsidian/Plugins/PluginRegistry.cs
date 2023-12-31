using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Services;
using System.Reflection;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(PluginManager pluginManager, EventDispatcher eventDispatcher, CommandHandler commandHandler, ILogger logger) : IPluginRegistry
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly EventDispatcher eventDispatcher = eventDispatcher;
    private readonly CommandHandler commandHandler = commandHandler;
    private readonly ILogger logger = logger;

    public IPluginRegistry MapCommand(string name, Delegate handler)
    {

        return this;
    }

    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate)
    {

        return this;
    }

    public IPluginRegistry MapCommands()
    {
        var executingAssembly = Assembly.GetExecutingAssembly();


        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {
        var executingAssembly = Assembly.GetExecutingAssembly();

        var pluginContainer = this.pluginManager.Plugins.First(x => x.PluginAssembly == executingAssembly);

        this.eventDispatcher.RegisterEvent(pluginContainer, contextDelegate, priority);
        return this;
    }

    public IPluginRegistry MapEvent(Delegate handler, Priority priority = Priority.Low) 
    {
        var executingAssembly = Assembly.GetExecutingAssembly();

        var pluginContainer = this.pluginManager.Plugins.First(x => x.PluginAssembly == executingAssembly);

        this.eventDispatcher.RegisterEvent(pluginContainer, handler, priority);
        return this;
    }

    public IPluginRegistry MapEvents()
    {
        var executingAssembly = Assembly.GetExecutingAssembly();

        var pluginContainer = this.pluginManager.Plugins.First(x => x.PluginAssembly == executingAssembly);

        this.eventDispatcher.RegisterEvents(pluginContainer);

        return this;
    }

    public IPluginRegistry RegisterArgumentHandler<T>(T parser) where T : BaseArgumentParser
    {
        this.commandHandler.AddArgumentParser(parser);

        return this;
    }
}
