using System.Runtime.Serialization;

namespace Obsidian.Commands.Framework.Exceptions;

public class DisallowedCommandIssuerException : Exception
{
    public CommandIssuers AllowedIssuers { get; set; }

    public DisallowedCommandIssuerException(CommandIssuers allowedIssuers)
    {
        AllowedIssuers = allowedIssuers;
    }

    public DisallowedCommandIssuerException(string message, CommandIssuers allowedIssuers) : base(message)
    {
        AllowedIssuers = allowedIssuers;
    }

    public DisallowedCommandIssuerException(string message, Exception innerException, CommandIssuers allowedIssuers) : base(message, innerException)
    {
        AllowedIssuers = allowedIssuers;
    }

    protected DisallowedCommandIssuerException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
