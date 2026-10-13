
namespace Obsidian.API.Inventory.DataComponents;

public sealed record class ProfileDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Profile;

    public override string Identifier => "minecraft:profile";

    private bool isComplete;

    public bool IsComplete
    {
        get => this.isComplete;
        init => this.isComplete = value;
    }

    private string? bodyTexture;

    public string? BodyTexture
    {
        get => this.bodyTexture;
        init => this.bodyTexture = value;
    }

    private string? capeTexture;

    public string? CapeTexture
    {
        get => this.capeTexture;
        init => this.capeTexture = value;
    }

    private string? elytraTexture;

    public string? ElytraTexture
    {
        get => this.elytraTexture;
        init => this.elytraTexture = value;
    }

    private int? model;

    public int? Model
    {
        get => this.model;
        init => this.model = value;
    }

    private string? username;

    public string? Username
    {
        get => this.username;
        init => this.username = value;
    }

    private Guid? id;

    public Guid? Id
    {
        get => this.id;
        init => this.id = value;
    }

    private ImmutableArray<SkinProperty> properties = [];

    public ImmutableArray<SkinProperty> Properties
    {
        get => this.properties;
        init => this.properties = value;
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.isComplete = reader.ReadBoolean();
        if (this.IsComplete)
        {
            this.id = reader.ReadGuid();
            this.username = reader.ReadString();
        }
        else
        {
            this.username = reader.ReadOptionalString();
            this.id = reader.ReadOptionalGuid();
        }

        this.properties = reader.ReadLengthPrefixedArray(() => SkinProperty.Read(reader)).ToImmutableArray();
        this.bodyTexture = reader.ReadOptionalString();
        this.capeTexture = reader.ReadOptionalString();
        this.elytraTexture = reader.ReadOptionalString();
        this.model = reader.ReadBoolean() ? reader.ReadVarInt() : null;
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
