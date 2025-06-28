using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Services;
using System.Reflection;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(PluginManager pluginManager, EventDispatcher eventDispatcher, 
    CommandHandler commandHandler, ILogger logger) : IPluginRegistry
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly EventDispatcher eventDispatcher = eventDispatcher;
    private readonly CommandHandler commandHandler = commandHandler;
    private readonly ILogger logger = logger;

    public IPluginRegistry MapCommand(string name, Delegate handler)
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.commandHandler.RegisterCommand(container, name, handler);
        return this;
    }

    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate)
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.commandHandler.RegisterCommand(container, name, contextDelegate);
        return this;
    }
    
    public IPluginRegistry MapCommands()
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.commandHandler.RegisterCommands(container);

        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.eventDispatcher.RegisterEvent(container, contextDelegate, priority);

        return this;
    }

    public IPluginRegistry MapEvent(Delegate handler, Priority priority = Priority.Low) 
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.eventDispatcher.RegisterEvent(container, handler, priority);

        return this;
    }

    public IPluginRegistry MapEvents()
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.eventDispatcher.RegisterEvents(container);

        return this;
    }

    public IPluginRegistry RegisterCommandArgumentHandler<T>(BaseArgumentParser<T> parser) 
    {
        this.commandHandler.TryAddArgumentParser(parser);

        return this;
    }
}
