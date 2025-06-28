using System.Numerics;

namespace Obsidian.API.Commands.ArgumentParsers;

public abstract partial class NumericArgumentParser<TNumber> : BaseArgumentParser<TNumber> where TNumber : struct,
    IConvertible,
    IMinMaxValue<TNumber>,
    INumber<TNumber>
{
    private static Type NumberType => typeof(TNumber);
    public NumberFlags Flags { get; private set; }

    public TNumber Min { get; }

    public TNumber Max { get; }

    protected NumericArgumentParser(TNumber min, TNumber max)
    {
        this.Min = min;
        this.Max = max;

        if (min != TNumber.MinValue)
            this.Flags |= NumberFlags.HasMinValue;
        if (max != TNumber.MaxValue)
            this.Flags |= NumberFlags.HasMaxValue;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteByte((sbyte)this.Flags);

        this.WriteNumbers(writer);
    }

    private void WriteNumbers(INetStreamWriter writer)
    {
        if (NumberType == typeof(int))
            this.WriteAsInt(writer);
        else if (NumberType == typeof(double))
            this.WriteAsDouble(writer);
        else if (NumberType == typeof(float))
            this.WriteAsSingle(writer);
        else if (NumberType == typeof(long))
            this.WriteAsLong(writer);
    }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class SignedIntArgumentParser : NumericArgumentParser<int>
{
    public SignedIntArgumentParser() : base(int.MinValue, int.MaxValue) { }
    public SignedIntArgumentParser(int min, int max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out int result)
        => int.TryParse(input, out result);
}

[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedByteArgumentParser : NumericArgumentParser<byte>
{
    public UnsignedByteArgumentParser() : base(byte.MinValue, byte.MaxValue) { }
    public UnsignedByteArgumentParser(byte min, byte max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out byte result)
        => byte.TryParse(input, out result);
}

[ArgumentParser("brigadier:integer")]
public sealed partial class SignedByteArgumentParser : NumericArgumentParser<sbyte>
{
    public SignedByteArgumentParser() : base(sbyte.MinValue, sbyte.MaxValue) { }
    public SignedByteArgumentParser(sbyte min, sbyte max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out sbyte result)
        => sbyte.TryParse(input, out result);
}

[ArgumentParser("brigadier:integer")]
public sealed partial class SignedShortArgumentParser : NumericArgumentParser<short>
{
    public SignedShortArgumentParser() : base(short.MinValue, short.MaxValue) { }
    public SignedShortArgumentParser(short min, short max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out short result)
        => short.TryParse(input, out result);
}

[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedShortArgumentParser : NumericArgumentParser<ushort>
{
    public UnsignedShortArgumentParser() : base(ushort.MinValue, ushort.MaxValue) { }
    public UnsignedShortArgumentParser(ushort min, ushort max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out ushort result)
        => ushort.TryParse(input, out result);
}

[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedIntArgumentParser : NumericArgumentParser<uint>
{
    public UnsignedIntArgumentParser() : base(uint.MinValue, uint.MaxValue) { }
    public UnsignedIntArgumentParser(uint min, uint max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out uint result)
        => uint.TryParse(input, out result);
}

[ArgumentParser("brigadier:long")]
public sealed partial class SignedLongArgumentParser : NumericArgumentParser<long>
{
    public SignedLongArgumentParser() : base(long.MinValue, long.MaxValue) { }
    public SignedLongArgumentParser(long min, long max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out long result)
        => long.TryParse(input, out result);
}

[ArgumentParser("brigadier:long")]
public sealed partial class UnsignedLongArgumentParser : NumericArgumentParser<ulong>
{
    public UnsignedLongArgumentParser() : base(ulong.MinValue, ulong.MaxValue) { }
    public UnsignedLongArgumentParser(ulong min, ulong max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out ulong result)
        => ulong.TryParse(input, out result);
}

[ArgumentParser("brigadier:float")]
public sealed partial class FloatArgumentParser : NumericArgumentParser<float>
{
    public FloatArgumentParser() : base(float.MinValue, float.MaxValue) { }
    public FloatArgumentParser(float min, float max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out float result)
        => float.TryParse(input, out result);
}

[ArgumentParser("brigadier:double")]
public sealed partial class DoubleArgumentParser : NumericArgumentParser<double>
{
    public DoubleArgumentParser() : base(double.MinValue, double.MaxValue) { }
    public DoubleArgumentParser(double min, double max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out double result)
        => double.TryParse(input, out result);
}

[ArgumentParser("brigadier:integer")]
public sealed partial class DecimalArgumentParser : NumericArgumentParser<decimal>
{
    public DecimalArgumentParser() : base(decimal.MinValue, decimal.MaxValue) { }
    public DecimalArgumentParser(decimal min, decimal max) : base(min, max) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out decimal result)
        => decimal.TryParse(input, out result);
}

[Flags]
public enum NumberFlags : byte
{
    None,
    HasMinValue = 1,
    HasMaxValue
}
