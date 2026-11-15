namespace Obsidian.API;

/// <summary>
/// Gives a command a description and usage text, shown in help and when the arguments match no overload.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CommandInfoAttribute : Attribute
{
    /// <summary>
    /// Gets the command's description.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// Gets the command's usage text, like <c>/tp &lt;player&gt;</c>, or an empty string.
    /// </summary>
    public string Usage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandInfoAttribute"/> class with no usage text.
    /// </summary>
    /// <param name="description">The command's description.</param>
    public CommandInfoAttribute(string description) : this(description, "")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandInfoAttribute"/> class.
    /// </summary>
    /// <param name="description">The command's description.</param>
    /// <param name="usage">The command's usage text.</param>
    public CommandInfoAttribute(string description, string usage)
    {
        Description = description;
        Usage = usage;
    }
}
