namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
public sealed record class TrimMaterialElement : INetworkSerializable<TrimMaterialElement>
{
    public required string AssetName { get; init; }

    public required TrimDescription Description { get; init; }

    public Dictionary<string, string>? OverrideArmorAssets { get; init; }

    public static TrimMaterialElement Read(INetStreamReader reader) => new()
    {
        AssetName = reader.ReadString(),
        OverrideArmorAssets = reader.ReadLengthPrefixedArray(() =>
        {
            return (rootId: reader.ReadString(), name: reader.ReadString());
        }).ToDictionary(x => x.rootId, x => x.name),
        Description = TrimDescription.Read(reader)
    };

    public static void Write(TrimMaterialElement element, INetStreamWriter writer)
    {
        writer.WriteString(element.AssetName);

        var count = element.OverrideArmorAssets?.Count ?? 0;
        writer.WriteVarInt(count);

        if (element.OverrideArmorAssets != null)
        {
            foreach (var (rootId, name) in element.OverrideArmorAssets)
            {
                writer.WriteString(rootId);
                writer.WriteString(name);
            }
        }

        TrimDescription.Write(element.Description, writer);
    }
}
