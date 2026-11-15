namespace Obsidian.API;

/// <summary>
/// Limits a command, or every command of a group, to the given kinds of sender. Commands without it accept any.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class IssuerScopeAttribute : Attribute
{
    /// <summary>
    /// Gets the kinds of sender allowed.
    /// </summary>
    public CommandIssuers Issuers { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IssuerScopeAttribute"/> class.
    /// </summary>
    /// <param name="issuers">The kinds of sender allowed.</param>
    public IssuerScopeAttribute(CommandIssuers issuers)
    {
        Issuers = issuers;
    }
}
