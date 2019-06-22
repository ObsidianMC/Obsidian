using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class CommandParser
    {
        public CommandParser(string identifier) => this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));

        public virtual async Task WriteAsync(MinecraftStream stream) => await stream.WriteIdentifierAsync(Identifier);

        public string Identifier { get; }
    }
}