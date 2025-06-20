using System.Numerics;

namespace Obsidian.API.Commands.Parsers;

public abstract partial class NumberCommandParser<TNumber> : CommandParser where TNumber : struct,
    IConvertible,
    IMinMaxValue<TNumber>,
    INumber<TNumber>
{
    private static Type NumberType => typeof(TNumber);
    public NumberFlags Flags { get; private set; }

    public TNumber Min { get; }

    public TNumber Max { get; }

    protected NumberCommandParser(TNumber min, TNumber max)
    {
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

[CommandParser("brigadier:double")]
public sealed partial class DoubleCommandParser : NumberCommandParser<double>
{
    public DoubleCommandParser() : base(double.MinValue, double.MaxValue) { }

    public DoubleCommandParser(double min, double max) : base(min, max) { }
}

[CommandParser("brigadier:float")]
public sealed partial class FloatCommandParser : NumberCommandParser<float>
{
    public FloatCommandParser() : base(float.MinValue, float.MaxValue) { }

    public FloatCommandParser(float min, float max) : base(min, max) { }
}

[CommandParser("brigadier:integer")]
public sealed partial class IntCommandParser : NumberCommandParser<int>
{
    public IntCommandParser() : base(int.MinValue, int.MaxValue) { }

    public IntCommandParser(int min, int max) : base(min, max)
    {
    }
}

[CommandParser("brigadier:long")]
public sealed partial class LongCommandParser : NumberCommandParser<long>
{
    public LongCommandParser() : base(long.MinValue, long.MaxValue) { }

    public LongCommandParser(long min, long max) : base(min, max) { }
}

[Flags]
public enum NumberFlags : byte
{
    None,
    HasMinValue = 1,
    HasMaxValue
}
