using System.Runtime.Serialization;

namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Thrown when a command runs from a kind of sender its <see cref="IssuerScopeAttribute"/>, or one of its groups',
/// doesn't allow.
/// </summary>
public class DisallowedCommandIssuerException : Exception
{
    /// <summary>
    /// Gets or sets the kinds of sender the command allows.
    /// </summary>
    public CommandIssuers AllowedIssuers { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedCommandIssuerException"/> class.
    /// </summary>
    /// <param name="allowedIssuers">The kinds of sender the command allows.</param>
    public DisallowedCommandIssuerException(CommandIssuers allowedIssuers)
    {
        AllowedIssuers = allowedIssuers;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedCommandIssuerException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="allowedIssuers">The kinds of sender the command allows.</param>
    public DisallowedCommandIssuerException(string message, CommandIssuers allowedIssuers) : base(message)
    {
        AllowedIssuers = allowedIssuers;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedCommandIssuerException"/> class with a message and the
    /// exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    /// <param name="allowedIssuers">The kinds of sender the command allows.</param>
    public DisallowedCommandIssuerException(string message, Exception innerException, CommandIssuers allowedIssuers) : base(message, innerException)
    {
        AllowedIssuers = allowedIssuers;
    }
}
