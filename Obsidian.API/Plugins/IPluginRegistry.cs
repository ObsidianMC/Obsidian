using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Networking;

namespace Obsidian.API.Plugins;
public interface IPluginRegistry
{
    public IPluginRegistry RegisterCommandArgumentHandler<T>(BaseArgumentParser<T> parser);

    public IPluginRegistry MapCommands();
    public IPluginRegistry MapCommand(string name, Delegate contextDelegate);
    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate);

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low)
        where TEventArgs : BaseMinecraftEventArgs;
    public IPluginRegistry MapEvent(Delegate contextDelegate, Priority priority = Priority.Low);
    public IPluginRegistry MapEvents();

    /// <summary>
    /// Registers a payload channel for <typeparamref name="TPayload"/>, negotiated with NeoForge clients during
    /// configuration. The registration is removed when the plugin unloads.
    /// </summary>
    /// <param name="phase">The connection phase the channel is used in.</param>
    /// <param name="direction">Which way the channel carries payloads.</param>
    /// <param name="handler">
    /// Handles payloads clients send on the channel; required to receive them, and not allowed for a clientbound
    /// channel.
    /// </param>
    /// <param name="version">The channel version; a NeoForge client mod must declare the same one.</param>
    /// <param name="optional">Whether clients without the channel may still connect.</param>
    public IPluginRegistry MapPayload<TPayload>(PayloadPhase phase, PayloadDirection direction,
        PayloadHandler<TPayload>? handler = null, string version = "1", bool optional = false)
        where TPayload : ICustomPayload<TPayload>;

    /// <summary>
    /// Registers an item, like a NeoForge mod's item: its identifier is in the plugin's namespace (its id), and it gets
    /// the next free item id. Registering it again, e.g. when the plugin reloads, gives back the same item with the
    /// new properties and behaviour.
    /// </summary>
    /// <param name="identifier">The item's identifier, like <c>myplugin:ruby</c>.</param>
    /// <param name="properties">The item's properties and behaviour; the defaults if null.</param>
    /// <returns>The item, to create stacks of.</returns>
    /// <remarks>
    /// Clients without the plugin can't show its items, so while a plugin with items is loaded, only NeoForge clients
    /// with a matching mod can join. When the plugin unloads its items lose their behaviour but stay registered.
    /// </remarks>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace, or isn't valid.</exception>
    public Item RegisterItem(string identifier, ItemProperties? properties = null);
}


//TODO better name maybe??
public delegate ValueTask ValueTaskContextDelegate<TContext>(TContext context);
