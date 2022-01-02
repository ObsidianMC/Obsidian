namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CommandAttribute : Attribute
{
    public string CommandName { get; }
    public string[] Aliases { get; }

    public CommandAttribute(string commandName, params string[] aliases)
    {
        CommandName = commandName;
        Aliases = aliases;
    }
}
