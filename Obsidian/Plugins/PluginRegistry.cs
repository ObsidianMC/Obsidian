using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Networking;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Services;
using System.Reflection;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(PluginManager pluginManager, EventDispatcher eventDispatcher, 
    CommandHandler commandHandler) : IPluginRegistry
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly EventDispatcher eventDispatcher = eventDispatcher;
    private readonly CommandHandler commandHandler = commandHandler;

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

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low)
        where TEventArgs : BaseMinecraftEventArgs
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

    public IPluginRegistry MapPayload<TPayload>(PayloadPhase phase, PayloadDirection direction,
        PayloadHandler<TPayload>? handler = null, string version = "1", bool optional = false)
        where TPayload : ICustomPayload<TPayload>
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);
        var server = (Server)this.pluginManager.server;

        server.PayloadChannels.Register(container, phase, direction, handler, version, optional);

        return this;
    }

    public Item RegisterItem(string identifier, ItemProperties? properties = null)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        // Like a NeoForge mod's, a plugin's content is in its own namespace, so plugins can't take each other's.
        if (!identifier.StartsWith($"{container.Info.Id}:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} isn't in the plugin's namespace, {container.Info.Id}.");

        // Vanilla's identifier rules, which clients parse the identifier with.
        var path = identifier[(container.Info.Id.Length + 1)..];
        if (path.Length == 0
            || !container.Info.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.')
            || !path.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.' or '/'))
            throw new ArgumentException($"{identifier} isn't a valid identifier.");

        properties ??= new ItemProperties();
        ArgumentOutOfRangeException.ThrowIfLessThan(properties.MaxStackSize, 1, nameof(properties));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(properties.MaxStackSize, 99, nameof(properties));

        var item = ItemsRegistry.Register(identifier);

        if (!ItemsRegistry.TryBind(item, properties, container, () => container.IsClosing))
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");

        return item;
    }

    public IPluginRegistry RegisterCommandArgumentHandler<T>(BaseArgumentParser<T> parser) 
    {
        this.commandHandler.TryAddArgumentParser(parser);

        return this;
    }
}
