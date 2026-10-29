using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record class TrimDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Trim;

    public override string Identifier => "minecraft:trim";

    private ChatMessage? materialDescription;

    public ChatMessage? MaterialDescription
    {
        get => ComponentCopies.Chat(this.materialDescription);
        init => this.materialDescription = ComponentCopies.Chat(value);
    }

    private ChatMessage? patternDescription;

    public ChatMessage? PatternDescription
    {
        get => ComponentCopies.Chat(this.patternDescription);
        init => this.patternDescription = ComponentCopies.Chat(value);
    }

    private int? materialRegistryId;

    public int? MaterialRegistryId
    {
        get => this.materialRegistryId;
        init => this.materialRegistryId = value;
    }

    private int? patternRegistryId;

    public int? PatternRegistryId
    {
        get => this.patternRegistryId;
        init => this.patternRegistryId = value;
    }

    private TrimMaterialElement material;

    public TrimMaterialElement Material
    {
        get => this.material;
        init => this.material = value;
    }

    private TrimPatternElement pattern;

    public TrimPatternElement Pattern
    {
        get => this.pattern;
        init => this.pattern = value;
    }

    private bool showInToolTip;

    public bool ShowInToolTip
    {
        get => this.showInToolTip;
        init => this.showInToolTip = value;
    }

    private static TrimDescription LegacyDescription(ChatMessage chat) => new() { Translate = chat.Translate ?? "", Color = chat.Color?.ToString() };
    private static ChatMessage ChatDescription(TrimDescription value) => new()
    { Translate = value.Translate, Color = value.Color is string color ? new HexColor(color) : null };

    protected override void ReadCore(INetStreamReader reader)
    {
        this.material = null;
        this.materialDescription = null;
        this.pattern = null;
        this.patternDescription = null;
        var material = reader.ReadVarInt();
        this.materialRegistryId = material == 0 ? null : material - 1;
        if (material == 0)
        {
            var palette = reader.ReadString();
            this.materialDescription = reader.ReadChat();
            this.material = new()
            {
                PaletteId = palette,
                Description = LegacyDescription(this.MaterialDescription)
            };
        }

        var pattern = reader.ReadVarInt();
        this.patternRegistryId = pattern == 0 ? null : pattern - 1;
        if (pattern == 0)
        {
            var asset = reader.ReadString();
            this.patternDescription = reader.ReadChat();
            this.pattern = new()
            {
                AssetId = asset,
                Description = LegacyDescription(this.PatternDescription),
                Decal = reader.ReadBoolean()
            };
        }
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.MaterialRegistryId is int material ? material + 1 : 0);
        if (this.MaterialRegistryId is null)
        {
            writer.WriteString(this.Material.PaletteId);
            writer.WriteChat(this.MaterialDescription ?? ChatDescription(this.Material.Description));
        }

        writer.WriteVarInt(this.PatternRegistryId is int pattern ? pattern + 1 : 0);
        if (this.PatternRegistryId is null)
        {
            writer.WriteString(this.Pattern.AssetId);
            writer.WriteChat(this.PatternDescription ?? ChatDescription(this.Pattern.Description));
            writer.WriteBoolean(this.Pattern.Decal);
        }
    }
}
