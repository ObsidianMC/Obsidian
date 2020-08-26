using Obsidian.Net;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Commands.Parsers
{
    public class DoubleCommandParser : CommandParser
    {
        public DoubleCommandParser() : base("") { }
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

        }
    }
}
