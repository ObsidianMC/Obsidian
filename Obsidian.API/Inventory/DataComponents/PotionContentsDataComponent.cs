using System.Runtime.InteropServices;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class PotionContentsDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.PotionContents;

    public override string Identifier => "minecraft:potion_contents";

    /// <summary>
    /// The potion whose effects the item has, if any.
    /// </summary>
    public Potion? Potion { get; set; }

    public int? CustomColor { get; set; }

    /// <summary>
    /// Effects on top of the potion's own.
    /// </summary>
    public ImmutableArray<PotionEffectData> CustomEffects { get; set; } = [];

    /// <summary>
    /// Replaces the potion's name in the item name, e.g. <c>water</c> for "Water Bottle".
    /// </summary>
    public string? CustomName { get; set; }

    public override void Read(INetStreamReader reader)
    {
        this.Potion = reader.ReadBoolean() ? (Potion)reader.ReadVarInt() : null;
        this.CustomColor = reader.ReadOptionalInt();
        this.CustomEffects = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadEffect(reader)));
        this.CustomName = reader.ReadOptionalString();
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
