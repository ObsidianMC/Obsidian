using System;

namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
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
