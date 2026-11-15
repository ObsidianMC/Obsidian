namespace Obsidian.API;

/// <summary>
/// Marks a method of a <see cref="Commands.CommandModuleBase"/> as a command. Further overloads must have the same
/// C# method name and be marked with <see cref="CommandOverloadAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CommandAttribute : Attribute
{
    /// <summary>
    /// Gets the command's name, typed after the slash (or after its group's name).
    /// </summary>
    public string CommandName { get; }
    /// <summary>
    /// Gets other names the command can be typed as.
    /// </summary>
    public string[] Aliases { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandAttribute"/> class.
    /// </summary>
    /// <param name="commandName">The command's name.</param>
    /// <param name="aliases">Other names the command can be typed as.</param>
    public CommandAttribute(string commandName, params string[] aliases)
    {
        CommandName = commandName;
        Aliases = aliases;
    }
}
