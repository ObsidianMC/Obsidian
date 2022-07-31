using Obsidian.Net;

namespace Obsidian.Commands.Parsers;

public class DoubleCommandParser : CommandParser
{
    public NumberFlags Flags { get; private set; }

    public double Min { get; }

    public double Max { get; }

    public DoubleCommandParser() : base(2, "brigadier:double") { }

    public DoubleCommandParser(double min, double max) : this()
    {
        if (min != double.MinValue)
            this.Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            this.Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteDoubleAsync(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteDoubleAsync(this.Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteDouble(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteDouble(this.Max);
    }
}

public class FloatCommandParser : CommandParser
{
    public NumberFlags Flags { get; set; }

    public float Min { get; set; }

    public float Max { get; set; }

    public FloatCommandParser() : base(1, "brigadier:float") { }

    public FloatCommandParser(float min, float max) : this()
    {
        if (min != double.MinValue)
            this.Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            this.Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteFloatAsync(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteFloatAsync(this.Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteFloat(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteFloat(this.Max);
    }
}

public class IntCommandParser : CommandParser
{
    public NumberFlags Flags { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }

    public IntCommandParser() : base(3, "brigadier:integer") { }

    public IntCommandParser(int min, int max) : this()
    {
        if (min != double.MinValue)
            this.Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            this.Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteIntAsync(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteIntAsync(this.Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteInt(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteInt(this.Max);
    }
}

public class LongCommandParser : CommandParser
{
    public NumberFlags Flags { get; set; }

    public long Min { get; set; }

    public long Max { get; set; }

    public LongCommandParser() : base(4, "brigadier:long") { }

    public LongCommandParser(long min, long max) : this()
    {
        if (min != double.MinValue)
            this.Flags |= NumberFlags.HasMinValue;
        if (max != double.MaxValue)
            this.Flags |= NumberFlags.HasMaxValue;
    }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            await stream.WriteLongAsync(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            await stream.WriteLongAsync(this.Max);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)this.Flags);

        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteLong(this.Min);
        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteLong(this.Max);
    }
}

[Flags]
public enum NumberFlags : byte
{
    None,
    HasMinValue = 1,
    HasMaxValue
}
