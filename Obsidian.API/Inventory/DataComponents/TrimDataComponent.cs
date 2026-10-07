using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class TrimDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Trim;

    public override string Identifier => "minecraft:trim";

    public ChatMessage? MaterialDescription { get; set; }
    public ChatMessage? PatternDescription { get; set; }
    public int? MaterialRegistryId { get; set; }
    public int? PatternRegistryId { get; set; }

    public TrimMaterialElement Material { get; set; }

    public TrimPatternElement Pattern { get; set; }

    public bool ShowInToolTip { get; set; }

    private static TrimDescription LegacyDescription(ChatMessage chat) => new() { Translate = chat.Translate ?? "", Color = chat.Color?.ToString() };
    private static ChatMessage ChatDescription(TrimDescription value) => new()
    { Translate = value.Translate, Color = value.Color is string color ? new HexColor(color) : null };

    public override void Read(INetStreamReader reader)
    {
        var material = reader.ReadVarInt();
        this.MaterialRegistryId = material == 0 ? null : material - 1;
        if (material == 0)
        {
            var asset = reader.ReadString();
            var overrides = reader.ReadLengthPrefixedArray(() => (Key: reader.ReadString(), Value: reader.ReadString())).ToDictionary(pair => pair.Key, pair => pair.Value);
            this.MaterialDescription = reader.ReadChat();
            this.Material = new() { AssetName = asset, OverrideArmorAssets = overrides, Description = LegacyDescription(this.MaterialDescription) };
        }
        var pattern = reader.ReadVarInt();
        this.PatternRegistryId = pattern == 0 ? null : pattern - 1;
        if (pattern == 0)
        {
            var asset = reader.ReadString(); this.PatternDescription = reader.ReadChat();
            this.Pattern = new() { AssetId = asset, Description = LegacyDescription(this.PatternDescription), Decal = reader.ReadBoolean() };
        }
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.MaterialRegistryId is int material ? material + 1 : 0);
        if (this.MaterialRegistryId is null)
        {
            writer.WriteString(this.Material.AssetName);
            writer.WriteVarInt(this.Material.OverrideArmorAssets?.Count ?? 0);
            if (this.Material.OverrideArmorAssets is not null)
                foreach (var (key, value) in this.Material.OverrideArmorAssets) { writer.WriteString(key); writer.WriteString(value); }
            writer.WriteChat(this.MaterialDescription ?? ChatDescription(this.Material.Description));
        }
        writer.WriteVarInt(this.PatternRegistryId is int pattern ? pattern + 1 : 0);
        if (this.PatternRegistryId is null)
        {
            writer.WriteString(this.Pattern.AssetId); writer.WriteChat(this.PatternDescription ?? ChatDescription(this.Pattern.Description));
            writer.WriteBoolean(this.Pattern.Decal);
        }
    }
}
