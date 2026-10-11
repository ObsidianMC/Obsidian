using System.Runtime.InteropServices;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class ProfileDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Profile;

    public override string Identifier => "minecraft:profile";

    public bool IsComplete { get; set; }
    public string? BodyTexture { get; set; }
    public string? CapeTexture { get; set; }
    public string? ElytraTexture { get; set; }
    public int? Model { get; set; }

    public string? Username { get; set; }

    public Guid? Id { get; set; }

    public ImmutableArray<SkinProperty> Properties { get; set; } = [];

    public override void Read(INetStreamReader reader)
    {
        this.IsComplete = reader.ReadBoolean();
        if (this.IsComplete)
        {
            this.Id = reader.ReadGuid();
            this.Username = reader.ReadString();
        }
        else
        {
            this.Username = reader.ReadOptionalString();
            this.Id = reader.ReadOptionalGuid();
        }

        this.Properties = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(() => SkinProperty.Read(reader)));
        this.BodyTexture = reader.ReadOptionalString();
        this.CapeTexture = reader.ReadOptionalString();
        this.ElytraTexture = reader.ReadOptionalString();
        this.Model = reader.ReadBoolean() ? reader.ReadVarInt() : null;
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.IsComplete);
        if (this.IsComplete)
        {
            writer.WriteUuid(this.Id!.Value);
            writer.WriteString(this.Username!);
        }
        else
        {
            writer.WriteOptional(this.Username);
            writer.WriteOptional(this.Id);
        }

        writer.WriteLengthPrefixedArray((value) => SkinProperty.Write(value, writer), this.Properties.AsSpan());
        writer.WriteOptional(this.BodyTexture);
        writer.WriteOptional(this.CapeTexture);
        writer.WriteOptional(this.ElytraTexture);
        writer.WriteBoolean(this.Model.HasValue);
        if (this.Model.HasValue)
            writer.WriteVarInt(this.Model.Value);
    }
}
