using Obsidian.API.Plugins;

namespace Obsidian.API;

public sealed class CommandContext(string message, ICommandSender commandSender, IPlayer player, IServer server)
{
    public IPlayer? Player { get; } = player;
    public IServer Server { get; } = server;
    public ICommandSender Sender { get; } = commandSender;
    public bool IsPlayer => Player != null;

    public PluginBase? Plugin { get; internal set; }
    internal string Message { get; } = message;
}
