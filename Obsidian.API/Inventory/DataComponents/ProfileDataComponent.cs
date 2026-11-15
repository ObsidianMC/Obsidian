
namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// The player profile a player head shows (<c>minecraft:profile</c>).
/// </summary>
public sealed record class ProfileDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.isComplete,
        this.bodyTexture,
        this.capeTexture,
        this.elytraTexture,
        this.model,
        this.username,
        this.id,
        ComponentValueKey.Sequence(this.properties)
    });

    /// <inheritdoc/>
    public bool Equals(ProfileDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Profile;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:profile";

    private bool isComplete;

    /// <summary>
    /// Gets whether the profile is complete, with both <see cref="Id"/> and <see cref="Username"/>; otherwise either
    /// may be missing and clients look the player up.
    /// </summary>
    public bool IsComplete
    {
        get => this.isComplete;
        init => this.isComplete = value;
    }

    private string? bodyTexture;

    /// <summary>
    /// Gets the id of a body texture that replaces the profile's skin, or null.
    /// </summary>
    public string? BodyTexture
    {
        get => this.bodyTexture;
        init => this.bodyTexture = value;
    }

    private string? capeTexture;

    /// <summary>
    /// Gets the id of a cape texture that replaces the profile's, or null.
    /// </summary>
    public string? CapeTexture
    {
        get => this.capeTexture;
        init => this.capeTexture = value;
    }

    private string? elytraTexture;

    /// <summary>
    /// Gets the id of an elytra texture that replaces the profile's, or null.
    /// </summary>
    public string? ElytraTexture
    {
        get => this.elytraTexture;
        init => this.elytraTexture = value;
    }

    private int? model;

    /// <summary>Optional skin model flag from PlayerModelType.STREAM_CODEC: 1 for slim, 0 for wide.</summary>
    public int? Model
    {
        get => this.model;
        init => this.model = value;
    }

    private string? username;

    /// <summary>
    /// Gets the player's name, or null.
    /// </summary>
    public string? Username
    {
        get => this.username;
        init => this.username = value;
    }

    private Guid? id;

    /// <summary>
    /// Gets the player's UUID, or null.
    /// </summary>
    public Guid? Id
    {
        get => this.id;
        init => this.id = value;
    }

    private ImmutableArray<SkinProperty> properties = [];

    /// <summary>
    /// Gets the profile's properties, like its signed skin textures.
    /// </summary>
    public ImmutableArray<SkinProperty> Properties
    {
        get => this.properties;
        init => this.properties = value;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
