using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.ArgumentParsers
{
    public abstract class BaseArgumentParser { }

    public abstract class BaseArgumentParser<T> : BaseArgumentParser
    {
        public abstract bool TryParseArgument(string input, out T result);

        public Type GetParserType()
        {
            return typeof(T);
        }
    }
}
