using Obsidian.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Commands.Framework.Exceptions
{
    public class NotAllowedCommandIssuerException : Exception
    {
        public CommandIssuer AllowedIssuers { get; set; }

        public NotAllowedCommandIssuerException(CommandIssuer allowedIssuers)
        {
            AllowedIssuers = allowedIssuers;
        }

        public NotAllowedCommandIssuerException(string message, CommandIssuer allowedIssuers) : base(message)
        {
            AllowedIssuers = allowedIssuers;
        }

        public NotAllowedCommandIssuerException(string message, Exception innerException, CommandIssuer allowedIssuers) : base(message, innerException)
        {
            AllowedIssuers = allowedIssuers;
        }

        protected NotAllowedCommandIssuerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
