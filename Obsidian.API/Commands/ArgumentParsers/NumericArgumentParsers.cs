using System.Globalization;
using System.Numerics;

namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads a number within <see cref="Min"/> and <see cref="Max"/>. The client is sent the bounds in its own number
/// type, so it flags numbers out of range, such as 300 for a <see cref="byte"/>, as vanilla does.
/// </summary>
public abstract partial class NumericArgumentParser<TNumber> : BaseArgumentParser<TNumber> where TNumber : struct,
    IConvertible,
    IMinMaxValue<TNumber>,
    INumber<TNumber>
{
    /// <summary>Which bounds were set explicitly, rather than being the limits of <typeparamref name="TNumber"/>.</summary>
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

    private bool IsIntegral => this.Identifier is "brigadier:integer" or "brigadier:long";

    /// <summary>
    /// Reads numbers as brigadier does: digits with an optional sign and, for decimal types, a '.' however the server's
    /// culture writes them.
    /// </summary>
    public override bool TryParseArgument(string input, CommandContext ctx, out TNumber result)
    {
        var style = this.IsIntegral
            ? NumberStyles.AllowLeadingSign
            : NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

        return TNumber.TryParse(input, style, CultureInfo.InvariantCulture, out result)
            && result >= this.Min && result <= this.Max;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        switch (this.Identifier)
        {
            case "brigadier:integer":
                WriteBounds(writer, int.CreateSaturating(this.Min), int.CreateSaturating(this.Max), writer.WriteInt);
                break;
            case "brigadier:long":
                WriteBounds(writer, long.CreateSaturating(this.Min), long.CreateSaturating(this.Max), writer.WriteLong);
                break;
            case "brigadier:float":
                WriteBounds(writer, float.CreateSaturating(this.Min), float.CreateSaturating(this.Max), writer.WriteSingle, explicitOnly: true);
                break;
            default:
                WriteBounds(writer, double.CreateSaturating(this.Min), double.CreateSaturating(this.Max), writer.WriteDouble, explicitOnly: true);
                break;
        }
    }

    /// <summary>
    /// Writes the flags and the bounds that narrow the wire type's range. For decimal types only explicit bounds are
    /// written, as their limits are no use to a player.
    /// </summary>
    private void WriteBounds<TWire>(INetStreamWriter writer, TWire min, TWire max, Action<TWire> write, bool explicitOnly = false)
        where TWire : INumber<TWire>, IMinMaxValue<TWire>
    {
        var flags = NumberFlags.None;

        if (min != TWire.MinValue && (!explicitOnly || this.Flags.HasFlag(NumberFlags.HasMinValue)))
            flags |= NumberFlags.HasMinValue;
        if (max != TWire.MaxValue && (!explicitOnly || this.Flags.HasFlag(NumberFlags.HasMaxValue)))
            flags |= NumberFlags.HasMaxValue;

        writer.WriteByte((sbyte)flags);

        if (flags.HasFlag(NumberFlags.HasMinValue))
            write(min);
        if (flags.HasFlag(NumberFlags.HasMaxValue))
            write(max);
    }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class SignedIntArgumentParser : NumericArgumentParser<int>
{
    public SignedIntArgumentParser() : base(int.MinValue, int.MaxValue) { }
    public SignedIntArgumentParser(int min, int max) : base(min, max) { }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedByteArgumentParser : NumericArgumentParser<byte>
{
    public UnsignedByteArgumentParser() : base(byte.MinValue, byte.MaxValue) { }
    public UnsignedByteArgumentParser(byte min, byte max) : base(min, max) { }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class SignedByteArgumentParser : NumericArgumentParser<sbyte>
{
    public SignedByteArgumentParser() : base(sbyte.MinValue, sbyte.MaxValue) { }
    public SignedByteArgumentParser(sbyte min, sbyte max) : base(min, max) { }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class SignedShortArgumentParser : NumericArgumentParser<short>
{
    public SignedShortArgumentParser() : base(short.MinValue, short.MaxValue) { }
    public SignedShortArgumentParser(short min, short max) : base(min, max) { }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedShortArgumentParser : NumericArgumentParser<ushort>
{
    public UnsignedShortArgumentParser() : base(ushort.MinValue, ushort.MaxValue) { }
    public UnsignedShortArgumentParser(ushort min, ushort max) : base(min, max) { }
}

[ArgumentParser("brigadier:integer")]
public sealed partial class UnsignedIntArgumentParser : NumericArgumentParser<uint>
{
    public UnsignedIntArgumentParser() : base(uint.MinValue, uint.MaxValue) { }
    public UnsignedIntArgumentParser(uint min, uint max) : base(min, max) { }
}

[ArgumentParser("brigadier:long")]
public sealed partial class SignedLongArgumentParser : NumericArgumentParser<long>
{
    public SignedLongArgumentParser() : base(long.MinValue, long.MaxValue) { }
    public SignedLongArgumentParser(long min, long max) : base(min, max) { }
}

[ArgumentParser("brigadier:long")]
public sealed partial class UnsignedLongArgumentParser : NumericArgumentParser<ulong>
{
    public UnsignedLongArgumentParser() : base(ulong.MinValue, ulong.MaxValue) { }
    public UnsignedLongArgumentParser(ulong min, ulong max) : base(min, max) { }
}

[ArgumentParser("brigadier:float")]
public sealed partial class FloatArgumentParser : NumericArgumentParser<float>
{
    public FloatArgumentParser() : base(float.MinValue, float.MaxValue) { }
    public FloatArgumentParser(float min, float max) : base(min, max) { }
}

[ArgumentParser("brigadier:double")]
public sealed partial class DoubleArgumentParser : NumericArgumentParser<double>
{
    public DoubleArgumentParser() : base(double.MinValue, double.MaxValue) { }
    public DoubleArgumentParser(double min, double max) : base(min, max) { }
}

[ArgumentParser("brigadier:double")]
public sealed partial class DecimalArgumentParser : NumericArgumentParser<decimal>
{
    public DecimalArgumentParser() : base(decimal.MinValue, decimal.MaxValue) { }
    public DecimalArgumentParser(decimal min, decimal max) : base(min, max) { }
}

[Flags]
public enum NumberFlags : byte
{
    None,
    HasMinValue = 1,
    HasMaxValue = 2
}
