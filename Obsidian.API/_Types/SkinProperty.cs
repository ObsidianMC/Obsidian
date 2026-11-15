namespace Obsidian.API;
/// <summary>A profile property of a player, such as <c>textures</c>, which holds their skin and cape.</summary>
public sealed class SkinProperty : INetworkSerializable<SkinProperty>
{
    /// <summary>Gets the property's name, such as <c>textures</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the property's value; for <c>textures</c>, Base64-encoded JSON.</summary>
    public required string Value { get; init; }
    /// <summary>Gets the Mojang signature of <see cref="Value"/>, or null if it is unsigned.</summary>
    public string? Signature { get; init; }

    /// <summary>Gets whether the property has a signature.</summary>
    public bool HasSignature => this.Signature is not null;

    /// <summary>Reads a property from a network stream.</summary>
    public static SkinProperty Read(INetStreamReader reader) => new()
    {
        Name = reader.ReadString(),
        Value = reader.ReadString(),
        Signature = reader.ReadOptionalString(),
    };

    /// <summary>Writes a property to a network stream.</summary>
    public static void Write(SkinProperty value, INetStreamWriter writer)
    {
        writer.WriteString(value.Name);
        writer.WriteString(value.Value);
        writer.WriteOptional(value.Signature);
    }
}
