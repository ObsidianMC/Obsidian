namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CommandInfoAttribute : Attribute
{
    public string Description { get; }
    public string Usage { get; }

    public CommandInfoAttribute(string description) : this(description, "")
    {
    }

    public CommandInfoAttribute(string description, string usage)
    {
        Description = description;
        Usage = usage;
    }
}
