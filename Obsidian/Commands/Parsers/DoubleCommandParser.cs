using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Commands.Parsers
{
    public class DoubleCommandParser : CommandParser
    {
        public DoubleCommandParser() : base(string.Empty)
        {
        }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);
        }
    }
}
