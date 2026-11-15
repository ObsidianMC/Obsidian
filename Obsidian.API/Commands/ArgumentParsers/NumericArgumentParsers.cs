using System.Numerics;

namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// The base class for parsers of numeric command arguments. Bounds are sent to the client only for
/// <see cref="int"/>, <see cref="long"/>, <see cref="float"/> and <see cref="double"/>.
/// </summary>
/// <typeparam name="TNumber">The numeric type parsed.</typeparam>
public abstract partial class NumericArgumentParser<TNumber> : BaseArgumentParser<TNumber> where TNumber : struct,
    IConvertible,
    IMinMaxValue<TNumber>,
    INumber<TNumber>
{
    private static Type NumberType => typeof(TNumber);
    /// <summary>
    /// Gets which of <see cref="Min"/> and <see cref="Max"/> differ from the type's limits. These flags are sent to the
    /// client even for types whose bound values aren't written; see <see cref="Write"/>.
    /// </summary>
    public NumberFlags Flags { get; private set; }

    /// <summary>
    /// Gets the stored lower bound, sent to the client only for <see cref="int"/>, <see cref="long"/>,
    /// <see cref="float"/> and <see cref="double"/>.
    /// </summary>
    /// <remarks>Server parsing checks the numeric type's range, not this bound.</remarks>
    public TNumber Min { get; }

    /// <summary>
    /// Gets the stored upper bound, sent to the client only for <see cref="int"/>, <see cref="long"/>,
    /// <see cref="float"/> and <see cref="double"/>.
    /// </summary>
    /// <remarks>Server parsing checks the numeric type's range, not this bound.</remarks>
    public TNumber Max { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NumericArgumentParser{TNumber}"/> class with the given bounds.
    /// </summary>
    /// <param name="min">The lower bound to store; see <see cref="Min"/> for client support.</param>
    /// <param name="max">The upper bound to store; see <see cref="Max"/> for client support.</param>
    protected NumericArgumentParser(TNumber min, TNumber max)
    {
        this.Min = min;
        this.Max = max;

        if (min != TNumber.MinValue)
            this.Flags |= NumberFlags.HasMinValue;
        if (max != TNumber.MaxValue)
            this.Flags |= NumberFlags.HasMaxValue;
    }

    /// <summary>
    /// Writes the parser's id and <see cref="Flags"/>. Bound values are written only for <see cref="int"/>,
    /// <see cref="long"/>, <see cref="float"/> and <see cref="double"/>. Other types with non-default bounds set the
    /// flags without writing the values, so their bounds aren't sent correctly to clients.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
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

/// <summary>
/// Reads a 32-bit signed integer (<see cref="int"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:integer")]
public sealed partial class SignedIntArgumentParser : NumericArgumentParser<int>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedIntArgumentParser"/> class that accepts any <see cref="int"/>.
    /// </summary>
    public SignedIntArgumentParser() : base(int.MinValue, int.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedIntArgumentParser"/> class with bounds the client enforces.
    /// </summary>
    /// <param name="min">The smallest value the client accepts.</param>
    /// <param name="max">The largest value the client accepts.</param>
    public SignedIntArgumentParser(int min, int max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out int result)
        => int.TryParse(input, out result);
}

/// <summary>
/// Reads an unsigned byte (<see cref="byte"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedByteArgumentParser : NumericArgumentParser<byte>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedByteArgumentParser"/> class that accepts any <see cref="byte"/>.
    /// </summary>
    public UnsignedByteArgumentParser() : base(byte.MinValue, byte.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedByteArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public UnsignedByteArgumentParser(byte min, byte max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out byte result)
        => byte.TryParse(input, out result);
}

/// <summary>
/// Reads a signed byte (<see cref="sbyte"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:integer")]
public sealed partial class SignedByteArgumentParser : NumericArgumentParser<sbyte>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedByteArgumentParser"/> class that accepts any <see cref="sbyte"/>.
    /// </summary>
    public SignedByteArgumentParser() : base(sbyte.MinValue, sbyte.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedByteArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public SignedByteArgumentParser(sbyte min, sbyte max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out sbyte result)
        => sbyte.TryParse(input, out result);
}

/// <summary>
/// Reads a 16-bit signed integer (<see cref="short"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:integer")]
public sealed partial class SignedShortArgumentParser : NumericArgumentParser<short>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedShortArgumentParser"/> class that accepts any <see cref="short"/>.
    /// </summary>
    public SignedShortArgumentParser() : base(short.MinValue, short.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedShortArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public SignedShortArgumentParser(short min, short max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out short result)
        => short.TryParse(input, out result);
}

/// <summary>
/// Reads a 16-bit unsigned integer (<see cref="ushort"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedShortArgumentParser : NumericArgumentParser<ushort>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedShortArgumentParser"/> class that accepts any <see cref="ushort"/>.
    /// </summary>
    public UnsignedShortArgumentParser() : base(ushort.MinValue, ushort.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedShortArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public UnsignedShortArgumentParser(ushort min, ushort max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out ushort result)
        => ushort.TryParse(input, out result);
}

/// <summary>
/// Reads a 32-bit unsigned integer (<see cref="uint"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedIntArgumentParser : NumericArgumentParser<uint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedIntArgumentParser"/> class that accepts any <see cref="uint"/>.
    /// </summary>
    public UnsignedIntArgumentParser() : base(uint.MinValue, uint.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedIntArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public UnsignedIntArgumentParser(uint min, uint max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out uint result)
        => uint.TryParse(input, out result);
}

/// <summary>
/// Reads a 64-bit signed integer (<see cref="long"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:long")]
public sealed partial class SignedLongArgumentParser : NumericArgumentParser<long>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedLongArgumentParser"/> class that accepts any <see cref="long"/>.
    /// </summary>
    public SignedLongArgumentParser() : base(long.MinValue, long.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedLongArgumentParser"/> class with bounds the client enforces.
    /// </summary>
    /// <param name="min">The smallest value the client accepts.</param>
    /// <param name="max">The largest value the client accepts.</param>
    public SignedLongArgumentParser(long min, long max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out long result)
        => long.TryParse(input, out result);
}

/// <summary>
/// Reads a 64-bit unsigned integer (<see cref="ulong"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:long")]
public sealed partial class UnsignedLongArgumentParser : NumericArgumentParser<ulong>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedLongArgumentParser"/> class that accepts any <see cref="ulong"/>.
    /// </summary>
    public UnsignedLongArgumentParser() : base(ulong.MinValue, ulong.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsignedLongArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public UnsignedLongArgumentParser(ulong min, ulong max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out ulong result)
        => ulong.TryParse(input, out result);
}

/// <summary>
/// Reads a single-precision number (<see cref="float"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:float")]
public sealed partial class FloatArgumentParser : NumericArgumentParser<float>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FloatArgumentParser"/> class that accepts any <see cref="float"/>.
    /// </summary>
    public FloatArgumentParser() : base(float.MinValue, float.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="FloatArgumentParser"/> class with bounds the client enforces.
    /// </summary>
    /// <param name="min">The smallest value the client accepts.</param>
    /// <param name="max">The largest value the client accepts.</param>
    public FloatArgumentParser(float min, float max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out float result)
        => float.TryParse(input, out result);
}

/// <summary>
/// Reads a double-precision number (<see cref="double"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:double")]
public sealed partial class DoubleArgumentParser : NumericArgumentParser<double>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleArgumentParser"/> class that accepts any <see cref="double"/>.
    /// </summary>
    public DoubleArgumentParser() : base(double.MinValue, double.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleArgumentParser"/> class with bounds the client enforces.
    /// </summary>
    /// <param name="min">The smallest value the client accepts.</param>
    /// <param name="max">The largest value the client accepts.</param>
    public DoubleArgumentParser(double min, double max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out double result)
        => double.TryParse(input, out result);
}

/// <summary>
/// Reads a decimal number (<see cref="decimal"/>) command argument.
/// </summary>
[ArgumentParser("brigadier:double")]
public sealed partial class DecimalArgumentParser : NumericArgumentParser<decimal>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DecimalArgumentParser"/> class that accepts any <see cref="decimal"/>.
    /// </summary>
    public DecimalArgumentParser() : base(decimal.MinValue, decimal.MaxValue) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="DecimalArgumentParser"/> class with stored bounds.
    /// </summary>
    /// <remarks>
    /// Non-default bounds set the flags without writing their values, so the bounds aren't sent correctly to clients.
    /// Server parsing checks only the numeric type's range, not these bounds.
    /// </remarks>
    /// <param name="min">The lower bound to store.</param>
    /// <param name="max">The upper bound to store.</param>
    public DecimalArgumentParser(decimal min, decimal max) : base(min, max) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out decimal result)
        => decimal.TryParse(input, out result);
}

/// <summary>
/// Which stored bounds differ from a numeric type's limits. See <see cref="NumericArgumentParser{TNumber}.Write"/>
/// for which types send the bound values to the client.
/// </summary>
[Flags]
public enum NumberFlags : byte
{
    /// <summary>
    /// Neither bound is set.
    /// </summary>
    None,
    /// <summary>
    /// The minimum is set.
    /// </summary>
    HasMinValue = 1,
    /// <summary>
    /// The maximum is set.
    /// </summary>
    HasMaxValue = 2
}
