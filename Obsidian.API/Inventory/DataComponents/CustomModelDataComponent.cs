namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Values resource packs' item models can select on (<c>minecraft:custom_model_data</c>).
/// </summary>
public sealed record CustomModelDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        ComponentValueKey.Sequence(this.floats),
        ComponentValueKey.Sequence(this.flags),
        ComponentValueKey.Sequence(this.strings),
        ComponentValueKey.Sequence(this.colors)
    });

    /// <inheritdoc/>
    public bool Equals(CustomModelDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.CustomModelData;
    /// <inheritdoc/>
    public override string Identifier => "minecraft:custom_model_data";
    private ImmutableArray<float> floats = [];

    /// <summary>
    /// Gets the float values.
    /// </summary>
    public ImmutableArray<float> Floats
    {
        get => this.floats;
        init => this.floats = value;
    }

    private ImmutableArray<bool> flags = [];

    /// <summary>
    /// Gets the boolean values.
    /// </summary>
    public ImmutableArray<bool> Flags
    {
        get => this.flags;
        init => this.flags = value;
    }

    private ImmutableArray<string> strings = [];

    /// <summary>
    /// Gets the string values.
    /// </summary>
    public ImmutableArray<string> Strings
    {
        get => this.strings;
        init => this.strings = value;
    }

    private ImmutableArray<int> colors = [];

    /// <summary>
    /// Gets the RGB color values.
    /// </summary>
    public ImmutableArray<int> Colors
    {
        get => this.colors;
        init => this.colors = value;
    }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.floats = reader.ReadLengthPrefixedArray(reader.ReadSingle).ToImmutableArray();
        this.flags = reader.ReadLengthPrefixedArray(reader.ReadBoolean).ToImmutableArray();
        this.strings = reader.ReadLengthPrefixedArray(() => reader.ReadString()).ToImmutableArray();
        this.colors = reader.ReadLengthPrefixedArray(reader.ReadInt).ToImmutableArray();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray(writer.WriteSingle, this.Floats.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteBoolean, this.Flags.AsSpan());
        writer.WriteLengthPrefixedArray(value => writer.WriteString(value), this.Strings.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteInt, this.Colors.AsSpan());
    }
}
