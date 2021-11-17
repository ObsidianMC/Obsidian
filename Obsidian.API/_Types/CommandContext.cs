using Obsidian.API.Plugins;

namespace Obsidian.API;

public sealed class CommandContext
{
    public IPlayer? Player { get; private set; }
    public IServer Server { get; private set; }
    public ICommandSender Sender { get; }
    public PluginBase? Plugin { get; internal set; }
    internal ReadOnlyMemory<char> Message { get; }

    public CommandContext(ReadOnlyMemory<char> message, ICommandSender commandSender, IPlayer player, IServer server)
    {
        Server = server;
        Sender = commandSender;
        Player = player;
        Message = message;
    }
}