namespace Obsidian.API.Commands.ArgumentParsers;

public sealed class SignedIntArgumentParser : BaseArgumentParser<int>
{
    public SignedIntArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out int result)
        => int.TryParse(input, out result);
}

public sealed class BoolArgumentParser : BaseArgumentParser<bool>
{
    public BoolArgumentParser() : base(0, "brigadier:bool") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out bool result)
        => bool.TryParse(input, out result);
}

public sealed class UnsignedByteArgumentParser : BaseArgumentParser<byte>
{
    public UnsignedByteArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out byte result)
        => byte.TryParse(input, out result);
}

public sealed class SignedByteArgumentParser : BaseArgumentParser<sbyte>
{
    public SignedByteArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out sbyte result)
        => sbyte.TryParse(input, out result);
}

public sealed class SignedShortArgumentParser : BaseArgumentParser<short>
{
    public SignedShortArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out short result)
        => short.TryParse(input, out result);
}

public sealed class UnsignedShortArgumentParser : BaseArgumentParser<ushort>
{
    public UnsignedShortArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out ushort result)
        => ushort.TryParse(input, out result);
}

public sealed class UnsignedIntArgumentParser : BaseArgumentParser<uint>
{
    public UnsignedIntArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out uint result)
        => uint.TryParse(input, out result);
}

public sealed class SignedLongArgumentParser : BaseArgumentParser<long>
{
    public SignedLongArgumentParser() : base(3, "brigadier:long") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out long result)
        => long.TryParse(input, out result);
}

public sealed class UnsignedLongArgumentParser : BaseArgumentParser<ulong>
{
    public UnsignedLongArgumentParser() : base(4, "brigadier:long") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out ulong result)
        => ulong.TryParse(input, out result);
}

public sealed class FloatArgumentParser : BaseArgumentParser<float>
{
    public FloatArgumentParser() : base(1, "brigadier:float") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out float result)
        => float.TryParse(input, out result);
}
public sealed class DoubleArgumentParser : BaseArgumentParser<double>
{
    public DoubleArgumentParser() : base(2, "brigadier:double") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out double result)
        => double.TryParse(input, out result);
}
public sealed class DecimalArgumentParser : BaseArgumentParser<decimal>
{
    public DecimalArgumentParser() : base(3, "brigadier:integer") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out decimal result)
        => decimal.TryParse(input, out result);
}
