using Obsidian.API.Plugins;
using System.Diagnostics;

namespace Obsidian.API.Commands;
/// <summary>
/// The base class for a class of commands. Its methods marked with <see cref="CommandAttribute"/> are commands, and
/// its nested classes marked with <see cref="CommandGroupAttribute"/> are groups of subcommands. A new instance is
/// created for every command run, with services injected into its <see cref="Plugins.InjectAttribute"/> properties.
/// </summary>
public abstract class CommandModuleBase
{
    private CommandContext? commandContext;

    /// <summary>
    /// Gets or sets the context of the command being run. The command handler sets it before the command runs.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value set is <see langword="null"/>.</exception>
    [CommandContext]
    public CommandContext CommandContext
    {
        get
        {
            if (commandContext is null)
                throw new UnreachableException();//TODO empty command context maybe??

            return this.commandContext;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            this.commandContext = value;
        }
    }

    /// <summary>
    /// Gets the player the command runs as, or <see langword="null"/> when no player does (from the console, say).
    /// </summary>
    public IPlayer? Player => this.CommandContext.Player;
    /// <summary>
    /// Gets the server the command runs on.
    /// </summary>
    public IServer Server => this.CommandContext.Server;
    /// <summary>
    /// Gets whoever sent the command, which feedback messages go to.
    /// </summary>
    public ICommandSender Sender => this.CommandContext.Sender;

    /// <summary>
    /// Gets a value indicating whether the command runs as a player.
    /// </summary>
    public bool IsPlayer => this.CommandContext.IsPlayer;

    /// <summary>
    /// Gets the plugin that registered the command, or <see langword="null"/> for the server's own commands.
    /// </summary>
    public PluginBase? Plugin => this.CommandContext.Plugin;
}
