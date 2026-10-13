namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Banner layers with either a connection registry ID or inline pattern data.</summary>
public sealed record BannerPatternsDataComponent() : SimpleDataComponent<ImmutableArray<BannerPatternLayer>>(
    DataComponentType.BannerPatterns, "minecraft:banner_patterns", (_, _) => { }, _ => [])
{
    private ImmutableArray<int?> registryIds = [];

    public ImmutableArray<int?> RegistryIds
    {
        get => this.registryIds;
        init => this.registryIds = value;
    }

    protected override void ReadCore(INetStreamReader reader)
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
        }).ToImmutableArray();
        this.value = entries.Select(entry => entry.layer).ToImmutableArray();
        this.registryIds = entries.Select(entry => entry.id).ToImmutableArray();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Value.Length);
        for (var i = 0; i < this.Value.Length; i++)
        {
            var id = i < this.RegistryIds.Length ? this.RegistryIds[i] : null;
            writer.WriteVarInt(id is int value ? value + 1 : 0);
            if (id is null)
            {
                writer.WriteString(this.Value[i].AssetId);
                writer.WriteString(this.Value[i].TranslationKey);
            }

            writer.WriteVarInt(this.Value[i].Color);
        }
    }
}
