namespace Obsidian.API.Inventory.DataComponents;

public sealed record CustomModelDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.CustomModelData;
    public override string Identifier => "minecraft:custom_model_data";
    public float[] Floats { get; set; } = [];
    public bool[] Flags { get; set; } = [];
    public string[] Strings { get; set; } = [];
    public int[] Colors { get; set; } = [];
    public override void Read(INetStreamReader reader)
    {
        this.Floats = reader.ReadLengthPrefixedArray(reader.ReadSingle);
        this.Flags = reader.ReadLengthPrefixedArray(reader.ReadBoolean);
        this.Strings = reader.ReadLengthPrefixedArray(() => reader.ReadString());
        this.Colors = reader.ReadLengthPrefixedArray(reader.ReadInt);
    }
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray(writer.WriteSingle, this.Floats);
        writer.WriteLengthPrefixedArray(writer.WriteBoolean, this.Flags);
        writer.WriteLengthPrefixedArray(value => writer.WriteString(value), this.Strings);
        writer.WriteLengthPrefixedArray(writer.WriteInt, this.Colors);
    }
}
