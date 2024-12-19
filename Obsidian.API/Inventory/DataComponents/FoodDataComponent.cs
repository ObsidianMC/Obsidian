namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Makes the item consumable.
/// </summary>
public sealed class FoodDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Food;

    public string Identifier => "minecraft:food";

    public required int Nutrition { get; set; }

    public required float SaturationModifier { get; set; }

    public required bool CanAlwaysEat { get; set; }

    public required float SecondsToEat { get; set; }

    public ItemStack? UsingConversTo { get; set; }

    public List<EffectWithProbability> Effects { get; set; } = [];

    public void Read(INetStreamReader reader)
    {
        this.Nutrition = reader.ReadVarInt();
        this.SaturationModifier = reader.ReadFloat();
        this.CanAlwaysEat = reader.ReadBoolean();
        this.SecondsToEat = reader.ReadFloat();
        this.UsingConversTo = reader.ReadItemStack();

        var count = reader.ReadVarInt();
        var effects = new List<EffectWithProbability>(count);

        for (int i = 0; i < count; i++)
        {
            var effect = reader.ReadPotionEffectData();
            var probability = reader.ReadFloat();

            effects[i] = new() { EffectData = effect, Probability = probability };
        }
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Nutrition);
        writer.WriteFloat(this.SaturationModifier);
        writer.WriteBoolean(this.CanAlwaysEat);
        writer.WriteFloat(this.SecondsToEat);
        writer.WriteItemStack(this.UsingConversTo);
        
        writer.WriteVarInt(this.Effects.Count);

        foreach (var effect in this.Effects)
        {
            PotionEffectData.Write(effect.EffectData, writer);
            writer.WriteFloat(effect.Probability);
        }
    }
}

public readonly struct EffectWithProbability
{
    public required PotionEffectData EffectData { get; init; }

    public required float Probability { get; init; }

}

public sealed record class EffectWithCurrentDuration
{
    public required PotionEffectData EffectData { get; init; }

    public required int CurrentDuration { get; set; }

}
