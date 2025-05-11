namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
public sealed class TrimPatternElement : INetworkSerializable<TrimPatternElement>
{
    public required TrimDescription Description { get; init; }

    public required string AssetId { get; init; }

    public required bool Decal { get; init; }

    public static TrimPatternElement Read(INetStreamReader reader) => new()
    {
        AssetId = reader.ReadString(),
        Description = TrimDescription.Read(reader),
        Decal = reader.ReadBoolean()
    };

    public static void Write(TrimPatternElement value, INetStreamWriter writer)
    {
        writer.WriteString(value.AssetId);

        TrimDescription.Write(value.Description, writer);
        writer.WriteBoolean(value.Decal);
    }
}
