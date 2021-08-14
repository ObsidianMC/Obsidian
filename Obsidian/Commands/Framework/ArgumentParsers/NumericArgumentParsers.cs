namespace Obsidian.API
{
    public class SignedIntArgumentParser : BaseArgumentParser<int>
    {
        public SignedIntArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out int result)
            => int.TryParse(input, out result);
    }

    public class BoolArgumentParser : BaseArgumentParser<bool>
    {
        public BoolArgumentParser() : base("brigadier:bool") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out bool result)
            => bool.TryParse(input, out result);
    }

    public class UnsignedByteArgumentParser : BaseArgumentParser<byte>
    {
        public UnsignedByteArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out byte result)
            => byte.TryParse(input, out result);
    }

    public class SignedByteArgumentParser : BaseArgumentParser<sbyte>
    {
        public SignedByteArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out sbyte result)
            => sbyte.TryParse(input, out result);
    }

    public class SignedShortArgumentParser : BaseArgumentParser<short>
    {
        public SignedShortArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out short result)
            => short.TryParse(input, out result);
    }

    public class UnsignedShortArgumentParser : BaseArgumentParser<ushort>
    {
        public UnsignedShortArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out ushort result)
            => ushort.TryParse(input, out result);
    }

    public class UnsignedIntArgumentParser : BaseArgumentParser<uint>
    {
        public UnsignedIntArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out uint result)
            => uint.TryParse(input, out result);
    }

    public class SignedLongArgumentParser : BaseArgumentParser<long>
    {
        public SignedLongArgumentParser() : base("brigadier:long") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out long result)
            => long.TryParse(input, out result);
    }

    public class UnsignedLongArgumentParser : BaseArgumentParser<ulong>
    {
        public UnsignedLongArgumentParser() : base("brigadier:long") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out ulong result)
            => ulong.TryParse(input, out result);
    }

    public class FloatArgumentParser : BaseArgumentParser<float>
    {
        public FloatArgumentParser() : base("brigadier:float") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out float result)
            => float.TryParse(input, out result);
    }
    public class DoubleArgumentParser : BaseArgumentParser<double>
    {
        public DoubleArgumentParser() : base("brigadier:double") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out double result)
            => double.TryParse(input, out result);
    }
    public class DecimalArgumentParser : BaseArgumentParser<decimal>
    {
        public DecimalArgumentParser() : base("brigadier:integer") { }
        public override bool TryParseArgument(string input, CommandContext ctx, out decimal result)
            => decimal.TryParse(input, out result);
    }
}
