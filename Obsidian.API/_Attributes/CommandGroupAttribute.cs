namespace Obsidian.API;

/// <summary>
/// Marks a class nested in a <see cref="Commands.CommandModuleBase"/> as a group of subcommands, typed after the group's name.
/// Its methods marked with <see cref="CommandAttribute"/> are the subcommands, and those marked with
/// <see cref="GroupCommandAttribute"/> run when the group's name is typed alone.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CommandGroupAttribute : Attribute
{
    /// <summary>
    /// Gets the group's name.
    /// </summary>
    public string GroupName { get; }
    /// <summary>
    /// Gets other names the group can be typed as.
    /// </summary>
    public string[] Aliases { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandGroupAttribute"/> class.
    /// </summary>
    /// <param name="groupName">The group's name.</param>
    /// <param name="aliases">Other names the group can be typed as.</param>
    public CommandGroupAttribute(string groupName, params string[] aliases)
    {
        GroupName = groupName;
        Aliases = aliases;
    }
}
