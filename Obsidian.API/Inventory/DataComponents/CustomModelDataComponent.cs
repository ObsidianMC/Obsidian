namespace Obsidian.API.Inventory.DataComponents;

public sealed record CustomModelDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.CustomModelData;
    public override string Identifier => "minecraft:custom_model_data";
    private ImmutableArray<float> floats = [];

    public ImmutableArray<float> Floats
    {
        get => this.floats;
        init => this.floats = value;
    }

    private ImmutableArray<bool> flags = [];

    public ImmutableArray<bool> Flags
    {
        get => this.flags;
        init => this.flags = value;
    }

    private ImmutableArray<string> strings = [];

    public ImmutableArray<string> Strings
    {
        get => this.strings;
        init => this.strings = value;
    }

    private ImmutableArray<int> colors = [];

    public ImmutableArray<int> Colors
    {
        get => this.colors;
        init => this.colors = value;
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.floats = reader.ReadLengthPrefixedArray(reader.ReadSingle).ToImmutableArray();
        this.flags = reader.ReadLengthPrefixedArray(reader.ReadBoolean).ToImmutableArray();
        this.strings = reader.ReadLengthPrefixedArray(() => reader.ReadString()).ToImmutableArray();
        this.colors = reader.ReadLengthPrefixedArray(reader.ReadInt).ToImmutableArray();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray(writer.WriteSingle, this.Floats.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteBoolean, this.Flags.AsSpan());
        writer.WriteLengthPrefixedArray(value => writer.WriteString(value), this.Strings.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteInt, this.Colors.AsSpan());
    }
}
