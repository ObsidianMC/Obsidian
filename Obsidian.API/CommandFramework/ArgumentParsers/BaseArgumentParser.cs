using System;

namespace Obsidian.CommandFramework.ArgumentParsers
{
    public abstract class BaseArgumentParser { }

    public abstract class BaseArgumentParser<T> : BaseArgumentParser
    {
        private string MinecraftType = "";
        public BaseArgumentParser(string minecraftType)
        {
            if (!MinecraftTypes.IsValidMcType(minecraftType))
                throw new Exception($"not a valid minecraft type! {minecraftType} in {this.GetType().Name}");

            this.MinecraftType = minecraftType;
        }
        public abstract bool TryParseArgument(string input, ObsidianContext ctx, out T result);

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
