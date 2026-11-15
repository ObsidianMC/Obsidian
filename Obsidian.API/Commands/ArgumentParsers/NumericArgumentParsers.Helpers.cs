namespace Obsidian.API.Commands.ArgumentParsers;
public partial class NumericArgumentParser<TNumber>
{
    /// <summary>
    /// Writes the bounds that <see cref="Flags"/> marks as 32-bit integers.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    protected void WriteAsInt(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteInt(this.Min.ToInt32(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteInt(this.Max.ToInt32(null));
    }

    /// <summary>
    /// Writes the bounds that <see cref="Flags"/> marks as doubles.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    protected void WriteAsDouble(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteDouble(this.Min.ToDouble(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteDouble(this.Max.ToDouble(null));
    }

    /// <summary>
    /// Writes the bounds that <see cref="Flags"/> marks as floats.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    protected void WriteAsSingle(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteSingle(this.Min.ToSingle(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteSingle(this.Max.ToSingle(null));
    }

    /// <summary>
    /// Writes the bounds that <see cref="Flags"/> marks as 64-bit integers.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    protected void WriteAsLong(INetStreamWriter writer)
    {
        if (this.Flags.HasFlag(NumberFlags.HasMinValue))
            writer.WriteLong(this.Min.ToInt64(null));

        if (this.Flags.HasFlag(NumberFlags.HasMaxValue))
            writer.WriteLong(this.Max.ToInt64(null));
    }
}
