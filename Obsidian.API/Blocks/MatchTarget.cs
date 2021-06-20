using System.Diagnostics;

namespace Obsidian.API.Blocks
{
    [DebuggerDisplay("{@base}:{numeric}")]
    internal readonly struct MatchTarget
    {
        public short Base { get; }
        public short Numeric { get; }

        public MatchTarget(short @base, short numeric)
        {
            Base = @base;
            Numeric = numeric;
        }
    }
}
