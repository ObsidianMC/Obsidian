namespace Obsidian.API.Inventory.DataComponents;
public sealed record class ProfileDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Profile;

    public string Identifier => "minecraft:profile";

    public string? Username { get; set; }

    public Guid? Id { get; set; }

    public List<SkinProperty> Properties { get; set; } = [];

    public void Read(INetStreamReader reader)
    {
        this.Username = reader.ReadOptionalString();
        this.Id = reader.ReadOptionalGuid();
        this.Properties = reader.ReadLengthPrefixedArray(() => SkinProperty.Read(reader));
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteOptional(this.Username);
        writer.WriteOptional(this.Id);

        writer.WriteLengthPrefixedArray((value) => SkinProperty.Write(value, writer), this.Properties);
    }
}
