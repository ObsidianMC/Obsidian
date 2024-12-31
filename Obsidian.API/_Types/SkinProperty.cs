namespace Obsidian.API;
public sealed class SkinProperty : INetworkSerializable<SkinProperty>
{
    public required string Name { get; set; }

    public required string Value { get; set; }
    public string? Signature { get; set; }

    public bool HasSignature => this.Signature != null;

    public static SkinProperty Read(INetStreamReader reader) => new()
    {
        Name = reader.ReadString(),
        Value = reader.ReadString(),
        Signature = reader.ReadOptionalString(),
    };

    public static void Write(SkinProperty value, INetStreamWriter writer)
    {
        writer.WriteString(value.Name);
        writer.WriteString(value.Value);
        writer.WriteOptional(value.Signature);
    }
}
