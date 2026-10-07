namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Banner layers with either a connection registry ID or inline pattern data.</summary>
public sealed record BannerPatternsDataComponent() : SimpleDataComponent<BannerPatternLayer[]>(
    DataComponentType.BannerPatterns, "minecraft:banner_patterns", (_, _) => { }, _ => [])
{
    public int?[] RegistryIds { get; set; } = [];
    public override void Read(INetStreamReader reader)
    {
        var entries = reader.ReadLengthPrefixedArray(() =>
        {
            var holder = reader.ReadVarInt();
            return (id: holder == 0 ? (int?)null : holder - 1, layer: new BannerPatternLayer
            {
                AssetId = holder == 0 ? reader.ReadString() : "",
                TranslationKey = holder == 0 ? reader.ReadString() : "",
                Color = reader.ReadVarInt<Dye>()
            });
        });
        this.Value = entries.Select(entry => entry.layer).ToArray();
        this.RegistryIds = entries.Select(entry => entry.id).ToArray();
    }
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Value.Length);
        for (var i = 0; i < this.Value.Length; i++)
        {
            var id = i < this.RegistryIds.Length ? this.RegistryIds[i] : null;
            writer.WriteVarInt(id is int value ? value + 1 : 0);
            if (id is null) { writer.WriteString(this.Value[i].AssetId); writer.WriteString(this.Value[i].TranslationKey); }
            writer.WriteVarInt(this.Value[i].Color);
        }
    }
}
