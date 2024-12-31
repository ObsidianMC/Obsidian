using System.Diagnostics.CodeAnalysis;

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

    [SetsRequiredMembers]
    internal FoodDataComponent() { }

    public void Read(INetStreamReader reader)
    {
        this.Nutrition = reader.ReadVarInt();
        this.SaturationModifier = reader.ReadFloat();
        this.CanAlwaysEat = reader.ReadBoolean();
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Nutrition);
        writer.WriteFloat(this.SaturationModifier);
        writer.WriteBoolean(this.CanAlwaysEat);
    }
}


