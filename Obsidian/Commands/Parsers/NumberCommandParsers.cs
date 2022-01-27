using Obsidian.Net;

namespace Obsidian.Commands.Parsers;

public class DoubleCommandParser : CommandParser
{
    public NumberFlags Flags { get; private set; }

    public double Min { get; }

    public double Max { get; }

    public DoubleCommandParser() : base("brigadier:double") { }

    public DoubleCommandParser(double min, double max) : this()
    {
        if (min != double.MinValue)
            Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteDoubleAsync(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteDoubleAsync(Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteDouble(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteDouble(Max);
    }
}

public class FloatCommandParser : CommandParser
{
    public NumberFlags Flags { get; set; }

    public float Min { get; set; }

    public float Max { get; set; }

    public FloatCommandParser() : base("brigadier:float") { }

    public FloatCommandParser(float min, float max) : this()
    {
        if (min != double.MinValue)
            Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteFloatAsync(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteFloatAsync(Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteFloat(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteFloat(Max);
    }
}

public class IntCommandParser : CommandParser
{
    public NumberFlags Flags { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }

    public IntCommandParser() : base("brigadier:integer") { }

    public IntCommandParser(int min, int max) : this()
    {
        if (min != double.MinValue)
            Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteIntAsync(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteIntAsync(Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteInt(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteInt(Max);
    }
}

public class LongCommandParser : CommandParser
{
    public NumberFlags Flags { get; set; }

    public long Min { get; set; }

    public long Max { get; set; }

    public LongCommandParser() : base("brigadier:long") { }

    public LongCommandParser(long min, long max) : this()
    {
        if (min != double.MinValue)
            Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteLongAsync(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteLongAsync(Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)Flags);

        if (Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteLong(Min);
        if (Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteLong(Max);
    }
}

[Flags]
public enum NumberFlags : byte
{
    None,
    HasMinValue = 1,
    HasMaxValue
}
