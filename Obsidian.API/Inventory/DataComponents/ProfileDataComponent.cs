namespace Obsidian.API.Inventory.DataComponents;
public sealed record class ProfileDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Profile;

    public override string Identifier => "minecraft:profile";

    public string? Username { get; set; }

    public Guid? Id { get; set; }

    public SkinProperty[] Properties { get; set; } = [];

    public override void Read(INetStreamReader reader)
    {
        this.Username = reader.ReadOptionalString();
        this.Id = reader.ReadOptionalGuid();
        this.Properties = reader.ReadLengthPrefixedArray(() => SkinProperty.Read(reader));
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteOptional(this.Username);
        writer.WriteOptional(this.Id);

        writer.WriteLengthPrefixedArray((value) => SkinProperty.Write(value, writer), this.Properties);
    }
}
