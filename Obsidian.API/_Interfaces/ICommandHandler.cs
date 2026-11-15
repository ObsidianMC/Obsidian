using Obsidian.API.Commands;
using Obsidian.API.Plugins;

namespace Obsidian.API;
/// <summary>
/// Runs, completes and keeps track of the server's commands and argument parsers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Gets the server's service provider.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }
    /// <summary>
    /// Runs the command in the context's message if it starts with the command prefix (<c>/</c>). A failed execution
    /// check is reported to the sender.
    /// </summary>
    /// <param name="ctx">The command's context, with the message and sender.</param>
    public Task ProcessCommand(CommandContext ctx);

    /// <summary>
    /// Suggests completions for the last word of the context's message, a command line typed up to the cursor with or
    /// without the command prefix.
    /// </summary>
    public Task<CommandCompletion> CompleteAsync(CommandContext ctx);

    /// <summary>
    /// Registers every top-level <see cref="Commands.CommandModuleBase"/> class in the plugin's assembly, or the
    /// server's when <paramref name="pluginContainer"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="pluginContainer">The plugin whose commands to register, or <see langword="null"/>.</param>
    public void RegisterCommands(IPluginContainer? pluginContainer = null);

    /// <summary>
    /// Returns whether a parser is registered for command parameters of <paramref name="type"/>. Enums always have one.
    /// </summary>
    /// <param name="type">The parameter type.</param>
    public bool IsValidArgumentType(Type type);

    /// <summary>
    /// Gets the parser for command parameters of <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The parameter type.</param>
    /// <returns>The parser.</returns>
    /// <exception cref="ArgumentException">No parser is registered for <paramref name="type"/>.</exception>
    public BaseArgumentParser GetArgumentParser(Type type);
    /// <summary>
    /// Gets the id and identifier of the vanilla argument type that parameters of <paramref name="type"/> are sent to
    /// clients as.
    /// </summary>
    /// <param name="type">The parameter type.</param>
    /// <returns>The argument type's numeric id and identifier.</returns>
    /// <exception cref="Exception">No parser is registered for <paramref name="type"/>.</exception>
    public (int id, string mctype) FindMinecraftType(Type type);

    /// <summary>
    /// Gets every registered command and group, subcommands included.
    /// </summary>
    public Command[] GetAllCommands();
}
