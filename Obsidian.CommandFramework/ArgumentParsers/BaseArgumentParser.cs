using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.ArgumentParsers
{
    public abstract class BaseArgumentParser { }

    public abstract class BaseArgumentParser<T> : BaseArgumentParser
    {
        private string MinecraftType = "";
        public BaseArgumentParser(string minecraftType)
        {
            this.MinecraftType = minecraftType;
        }
        public abstract bool TryParseArgument(string input, BaseCommandContext ctx, out T result);

        public string GetParserIdentifier()
        {
            return MinecraftType;
        }
        public Type GetParserType()
        {
            return typeof(T);
        }
    }
}
