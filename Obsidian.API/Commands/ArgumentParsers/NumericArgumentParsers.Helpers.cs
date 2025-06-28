namespace Obsidian.API.Commands.ArgumentParsers;
public partial class NumericArgumentParser<TNumber>
{
    protected void WriteAsInt(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteInt(this.Min.ToInt32(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteInt(this.Max.ToInt32(null));
    }

    protected void WriteAsDouble(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteDouble(this.Min.ToDouble(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteDouble(this.Max.ToDouble(null));
    }

    protected void WriteAsSingle(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteSingle(this.Min.ToSingle(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteSingle(this.Max.ToSingle(null));
    }

    protected void WriteAsLong(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteLong(this.Min.ToInt64(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteLong(this.Max.ToInt64(null));
    }
}
