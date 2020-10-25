using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.ArgumentParsers
{
    public class SignedIntArgumentParser : BaseArgumentParser<int>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out int result)
            => int.TryParse(input, out result);
    }

    public class BoolArgumentParser : BaseArgumentParser<bool>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out bool result)
            => bool.TryParse(input, out result);
    }

    public class UnsignedByteArgumentParser : BaseArgumentParser<byte>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out byte result)
            => byte.TryParse(input, out result);
    }

    public class SignedByteArgumentParser : BaseArgumentParser<sbyte>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out sbyte result)
            => sbyte.TryParse(input, out result);
    }

    public class SignedShortArgumentParser : BaseArgumentParser<short>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out short result)
            => short.TryParse(input, out result);
    }

    public class UnsignedShortArgumentParser : BaseArgumentParser<ushort>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out ushort result)
            => ushort.TryParse(input, out result);
    }

    public class UnsignedIntArgumentParser : BaseArgumentParser<uint>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out uint result)
            => uint.TryParse(input, out result);
    }

    public class SignedLongArgumentParser : BaseArgumentParser<long>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out long result)
            => long.TryParse(input, out result);
    }

    public class UnsignedLongArgumentParser : BaseArgumentParser<ulong>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out ulong result)
            => ulong.TryParse(input, out result);
    }

    public class FloatArgumentParser : BaseArgumentParser<float>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out float result)
            => float.TryParse(input, out result);
    }
    public class DoubleArgumentParser : BaseArgumentParser<double>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out double result)
            => double.TryParse(input, out result);
    }
    public class DecimalArgumentParser : BaseArgumentParser<decimal>
    {
        public override bool TryParseArgument(string input, BaseCommandContext ctx, out decimal result)
            => decimal.TryParse(input, out result);
    }
}
