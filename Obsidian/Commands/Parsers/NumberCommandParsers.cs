using System.Numerics;

namespace Obsidian.Commands.Parsers;

public abstract partial class NumberCommandParser<TNumber> : CommandParser where TNumber : struct,
    IConvertible,
    IMinMaxValue<TNumber>,
    INumber<TNumber>
{
    private static Type NumberType => typeof(TNumber);
    public NumberFlags Flags { get; private set; }

    public TNumber Min { get; }

    public TNumber Max { get; }

    protected NumberCommandParser(int id, string identifier,
        TNumber min, TNumber max) : base(id, identifier)
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

public sealed class DoubleCommandParser : NumberCommandParser<double>
{
    public DoubleCommandParser() : base(2, "brigadier:double", double.MinValue, double.MaxValue) { }

    public DoubleCommandParser(double min, double max) : base(2, "brigadier:double", min, max) { }
}

public sealed class FloatCommandParser : NumberCommandParser<float>
{
    public FloatCommandParser() : base(1, "brigadier:float", float.MinValue, float.MaxValue) { }

    public FloatCommandParser(float min, float max) : base(1, "brigadier:float", min, max) { }
}

public sealed class IntCommandParser : NumberCommandParser<int>
{
    public IntCommandParser() : base(3, "brigadier:integer", int.MinValue, int.MaxValue) { }

    public IntCommandParser(int min, int max) : base(3, "brigadier:integer", min, max)
    {
    }
}

public sealed class LongCommandParser : NumberCommandParser<long>
{
    public LongCommandParser() : base(4, "brigadier:long", long.MinValue, long.MaxValue) { }

    public LongCommandParser(long min, long max) : base(4, "brigadier:long", min, max) { }
}

[Flags]
public enum NumberFlags : byte
{
    None,
    HasMinValue = 1,
    HasMaxValue
}
