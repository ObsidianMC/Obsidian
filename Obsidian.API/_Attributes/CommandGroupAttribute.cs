namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CommandGroupAttribute : Attribute
{
    public string GroupName { get; }
    public string[] Aliases { get; }

    public CommandGroupAttribute(string groupName, params string[] aliases)
    {
        GroupName = groupName;
        Aliases = aliases;
    }
}
