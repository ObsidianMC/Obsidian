using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// An armour trim (<c>minecraft:trim</c>): its material and pattern, each by registry id or inline.
/// </summary>
public sealed record class TrimDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.materialRegistryId,
        this.patternRegistryId,
        this.material,
        this.pattern,
        this.materialDescription,
        this.patternDescription,
        this.showInToolTip
    });

    /// <inheritdoc/>
    public bool Equals(TrimDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Trim;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:trim";

    private ChatMessage? materialDescription;

    /// <summary>
    /// Gets the material's name as the tooltip shows it, for an inline material.
    /// </summary>
    public ChatMessage? MaterialDescription
    {
        get => ComponentCopies.Chat(this.materialDescription);
        init => this.materialDescription = ComponentCopies.Chat(value);
    }

    private ChatMessage? patternDescription;

    /// <summary>
    /// Gets the pattern's name as the tooltip shows it, for an inline pattern.
    /// </summary>
    public ChatMessage? PatternDescription
    {
        get => ComponentCopies.Chat(this.patternDescription);
        init => this.patternDescription = ComponentCopies.Chat(value);
    }

    private int? materialRegistryId;

    /// <summary>
    /// Gets the material's id in the client's trim material registry, or null to send <see cref="Material"/> inline.
    /// </summary>
    public int? MaterialRegistryId
    {
        get => this.materialRegistryId;
        init => this.materialRegistryId = value;
    }

    private int? patternRegistryId;

    /// <summary>
    /// Gets the pattern's id in the client's trim pattern registry, or null to send <see cref="Pattern"/> inline.
    /// </summary>
    public int? PatternRegistryId
    {
        get => this.patternRegistryId;
        init => this.patternRegistryId = value;
    }

    private TrimMaterialElement material;

    /// <summary>
    /// Gets the inline material, used when <see cref="MaterialRegistryId"/> is null.
    /// </summary>
    public TrimMaterialElement Material
    {
        get => this.material;
        init => this.material = value;
    }

    private TrimPatternElement pattern;

    /// <summary>
    /// Gets the inline pattern, used when <see cref="PatternRegistryId"/> is null.
    /// </summary>
    public TrimPatternElement Pattern
    {
        get => this.pattern;
        init => this.pattern = value;
    }

    private bool showInToolTip;

    /// <summary>
    /// Gets whether the tooltip shows the trim. Neither sent to clients nor saved.
    /// </summary>
    public bool ShowInToolTip
    {
        get => this.showInToolTip;
        init => this.showInToolTip = value;
    }

    private static TrimDescription LegacyDescription(ChatMessage chat) => new() { Translate = chat.Translate ?? "", Color = chat.Color?.ToString() };
    private static ChatMessage ChatDescription(TrimDescription value) => new()
    { Translate = value.Translate, Color = value.Color is string color ? new HexColor(color) : null };

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
