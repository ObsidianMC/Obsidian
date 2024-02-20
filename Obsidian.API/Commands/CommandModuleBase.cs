using Obsidian.API.Plugins;
using System.Diagnostics;

namespace Obsidian.API.Commands;
public abstract class CommandModuleBase
{
    private CommandContext? commandContext;

    [CommandContext]
    public CommandContext CommandContext
    {
        get
        {
            if (commandContext == null)
                throw new UnreachableException();//TODO empty command context maybe??

            return this.commandContext;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            this.commandContext = value;
        }
    }

    public IPlayer? Player => this.CommandContext.Player;
    public IServer Server => this.CommandContext.Server;
    public ICommandSender Sender => this.CommandContext.Sender;

    public bool IsPlayer => this.CommandContext.IsPlayer;

    public PluginBase? Plugin => this.CommandContext.Plugin;
}
