namespace Obsidian.API.Inventory.DataComponents;
public sealed record class PotionContentsDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.PotionContents;

    public override string Identifier => "minecraft:potion_contents";

    public Potion? Potion { get; set; }

    public int? CustomColor { get; set; }

    public PotionEffectData[] CustomEffects { get; set; }

    public string? CustomName { get; set; }

    public override void Read(INetStreamReader reader)
    {
        var hasPotion = reader.ReadBoolean();
        if(hasPotion)
        {
            this.Potion = new()
            {
                Name = reader.ReadString(),
                Effects = reader.ReadLengthPrefixedArray(reader.ReadPotionEffectData)
            };
        }

        this.CustomColor = reader.ReadOptionalInt();
        this.CustomEffects = reader.ReadLengthPrefixedArray(reader.ReadPotionEffectData);
        this.CustomName = reader.ReadOptionalString();
    }

    public override void Write(INetStreamWriter writer)
    {
        if (this.Potion is Potion potion)
        {
            writer.WriteString(potion.Name);

            writer.WriteLengthPrefixedArray((effect) => PotionEffectData.Write(effect, writer), potion.Effects);
        }

        writer.WriteOptional(this.CustomColor);

        writer.WriteLengthPrefixedArray((value) => PotionEffectData.Write(value, writer), this.CustomEffects);

        writer.WriteOptional(this.CustomName);
    }
}

public readonly record struct Potion
{
    public required string Name { get; init; }
    public required PotionEffectData[] Effects { get; init; }
}
