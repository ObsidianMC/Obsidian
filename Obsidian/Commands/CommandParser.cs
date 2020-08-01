using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class CommandParser
    {
        private string Identifier { get; }

        public CommandParser(string identifier) => this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));

        public async Task WriteAsync(MinecraftStream stream) => await stream.WriteStringAsync(Identifier);
    }
}