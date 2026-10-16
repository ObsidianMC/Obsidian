using Obsidian.API.Plugins;

namespace Obsidian.API;

public sealed class CommandContext(string message, ICommandSender commandSender, IPlayer? player, IServer server)
{
    public IPlayer? Player { get; } = player;

    /// <summary>
    /// The server the command runs on. A plugin's command gets the plugin's own <see cref="IServer"/>, whose additions
    /// are the plugin's (see <see cref="Events.BaseMinecraftEventArgs.Server"/>).
    /// </summary>
    public IServer Server => ServerScope.Resolve(server);

    public ICommandSender Sender { get; } = commandSender;
    public bool IsPlayer => Player is not null;

    public PluginBase? Plugin { get; internal set; }
    internal string Message { get; } = message;
}
