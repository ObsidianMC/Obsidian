using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class CommandParser
    {
        private string Identifier { get; }

        public CommandParser(string identifier) => this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));

        public virtual Task WriteAsync(MinecraftStream stream) => stream.WriteStringAsync(this.Identifier);

        public override string ToString()
        {
            return this.Identifier;
        }
    }
}