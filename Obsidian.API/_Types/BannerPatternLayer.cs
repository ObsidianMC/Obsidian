namespace Obsidian.API;
public sealed record class BannerPatternLayer : INetworkSerializable<BannerPatternLayer>
{
    public required string AssetId { get; init; }
    public required string TranslationKey { get; init; }

    public required Dye Color { get; init; }

    public static BannerPatternLayer Read(INetStreamReader reader) => new()
    {
        AssetId = reader.ReadString(),
        TranslationKey = reader.ReadString(),
        Color = reader.ReadVarInt<Dye>(),
    };

    public static void Write(BannerPatternLayer value, INetStreamWriter writer)
    {
        writer.WriteString(value.AssetId);
        writer.WriteString(value.TranslationKey);
        writer.WriteVarInt(value.Color);
    }
}
