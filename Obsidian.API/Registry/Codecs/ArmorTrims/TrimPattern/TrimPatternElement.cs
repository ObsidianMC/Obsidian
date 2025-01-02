using Obsidian.API.Registries;

namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
public sealed class TrimPatternElement : INetworkSerializable<TrimPatternElement>
{
    public required string TemplateItem { get; init; }

    public required TrimDescription Description { get; init; }

    public required string AssetId { get; init; }

    public required bool Decal { get; init; }

    public static TrimPatternElement Read(INetStreamReader reader) => new()
    {
        AssetId = reader.ReadString(),
        TemplateItem = ItemsRegistry.Get(reader).UnlocalizedName,
        Description = TrimDescription.Read(reader),
        Decal = reader.ReadBoolean()
    };

    public static void Write(TrimPatternElement value, INetStreamWriter writer)
    {
        writer.WriteString(value.AssetId);
        writer.WriteVarInt(ItemsRegistry.Get(value.TemplateItem).Id);

        TrimDescription.Write(value.Description, writer);
        writer.WriteBoolean(value.Decal);
    }
}
