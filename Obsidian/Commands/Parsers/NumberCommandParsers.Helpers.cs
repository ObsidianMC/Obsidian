using Obsidian.Net;

namespace Obsidian.Commands.Parsers;
public partial class NumberCommandParser<TNumber>
{
    protected void WriteAsInt(MinecraftStream stream)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteInt(this.Min.ToInt32(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteInt(this.Max.ToInt32(null));
    }

    protected void WriteAsDouble(MinecraftStream stream)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteDouble(this.Min.ToDouble(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteDouble(this.Max.ToDouble(null));
    }

    protected void WriteAsSingle(MinecraftStream stream)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteFloat(this.Min.ToSingle(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteFloat(this.Max.ToSingle(null));
    }

    protected void WriteAsLong(MinecraftStream stream)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            stream.WriteLong(this.Min.ToInt64(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            stream.WriteLong(this.Max.ToInt64(null));
    }
}
