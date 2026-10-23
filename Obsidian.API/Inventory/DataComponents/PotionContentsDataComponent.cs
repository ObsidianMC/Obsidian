
namespace Obsidian.API.Inventory.DataComponents;

public sealed record class PotionContentsDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.PotionContents;

    public override string Identifier => "minecraft:potion_contents";

    /// <summary>
    /// The potion whose effects the item has, if any.
    /// </summary>
    private Potion? potion;

    public Potion? Potion
    {
        get => this.potion;
        init => this.potion = value;
    }

    private int? customColor;

    public int? CustomColor
    {
        get => this.customColor;
        init => this.customColor = value;
    }

    /// <summary>
    /// Effects on top of the potion's own.
    /// </summary>
    private ImmutableArray<PotionEffectData> customEffects = [];

    public ImmutableArray<PotionEffectData> CustomEffects
    {
        get => this.customEffects;
        init => this.customEffects = value;
    }

    /// <summary>
    /// Replaces the potion's name in the item name, e.g. <c>water</c> for "Water Bottle".
    /// </summary>
    private string? customName;

    public string? CustomName
    {
        get => this.customName;
        init => this.customName = value;
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.potion = reader.ReadBoolean() ? (Potion)reader.ReadVarInt() : null;
        this.customColor = reader.ReadOptionalInt();
        this.customEffects = reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadEffect(reader))
            .ToImmutableArray();
        this.customName = reader.ReadOptionalString();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.Potion is not null);
        if (this.Potion is not null)
            writer.WriteVarInt((int)this.Potion.Value);

        writer.WriteOptional(this.CustomColor);
        writer.WriteLengthPrefixedArray((effect) => ComponentValueCodecs.WriteEffect(effect, writer), this.CustomEffects.AsSpan());
        writer.WriteOptional(this.CustomName);
    }
}
