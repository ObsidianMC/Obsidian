namespace Obsidian.API;
/// <summary>One pattern layer of a banner or shield.</summary>
public sealed record class BannerPatternLayer : INetworkSerializable<BannerPatternLayer>
{
    /// <summary>Gets the pattern's asset id, such as <c>minecraft:stripe_bottom</c>.</summary>
    public required string AssetId { get; init; }
    /// <summary>Gets the translation key of the pattern's name.</summary>
    public required string TranslationKey { get; init; }

    /// <summary>Gets the color the pattern is drawn in.</summary>
    public required Dye Color { get; init; }

    /// <summary>Reads a layer from a network stream.</summary>
    public static BannerPatternLayer Read(INetStreamReader reader) => new()
    {
        AssetId = reader.ReadString(),
        TranslationKey = reader.ReadString(),
        Color = reader.ReadVarInt<Dye>(),
    };

    /// <summary>Writes a layer to a network stream.</summary>
    public static void Write(BannerPatternLayer value, INetStreamWriter writer)
    {
        writer.WriteString(value.AssetId);
        writer.WriteString(value.TranslationKey);
        writer.WriteVarInt(value.Color);
    }
}
