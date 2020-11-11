using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.ArgumentParsers
{
    public class StringArgumentParser : BaseArgumentParser<string>
    {
        public StringArgumentParser() : base("brigadier:string") { }
        public override bool TryParseArgument(string input, ObsidianContext ctx, out string result)
        {
            result = input;
            return true;
        }
    }

    public class GuidArgumentParser : BaseArgumentParser<Guid>
    {
        public GuidArgumentParser() : base("minecraft:uuid") { }
        public override bool TryParseArgument(string input, ObsidianContext ctx, out Guid result)
        {
            return Guid.TryParse(input, out result);
        }
    }
}
