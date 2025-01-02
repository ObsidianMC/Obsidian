namespace Obsidian.API;
public sealed record class BannerPatternLayer : INetworkSerializable<BannerPatternLayer>
{
    public required string AssetId { get; set; }
    public required string TranslationKey { get; set; }

    public required Dye Color { get; set; }

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
