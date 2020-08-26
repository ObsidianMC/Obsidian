using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class CommandParser
    {
        private string Identifier { get; }

        public CommandParser(string identifier) => this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));

        public virtual async Task WriteAsync(MinecraftStream stream) => await stream.WriteStringAsync(this.Identifier);

        public override string ToString()
        {
            return this.Identifier;
        }
    }
}