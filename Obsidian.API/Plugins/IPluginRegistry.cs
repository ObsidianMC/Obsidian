using Obsidian.API.Events;
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
}


//TODO better name maybe??
public delegate ValueTask ValueTaskContextDelegate<TContext>(TContext context);
