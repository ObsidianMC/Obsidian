using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Makes the item consumable.
/// </summary>
public sealed record class FoodDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Food;

    public override string Identifier => "minecraft:food";

    public required int Nutrition { get; set; }

    public required float SaturationModifier { get; set; }

    public required bool CanAlwaysEat { get; set; }

    [SetsRequiredMembers]
    internal FoodDataComponent() { }

    public override void Read(INetStreamReader reader)
    {
        this.Nutrition = reader.ReadVarInt();
        this.SaturationModifier = reader.ReadSingle();
        this.CanAlwaysEat = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Nutrition);
        writer.WriteSingle(this.SaturationModifier);
        writer.WriteBoolean(this.CanAlwaysEat);
    }
}


