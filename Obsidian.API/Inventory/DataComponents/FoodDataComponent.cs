using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Makes the item consumable.
/// </summary>
public sealed record class FoodDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Food;

    public override string Identifier => "minecraft:food";

    private int nutrition;

    public required int Nutrition
    {
        get => this.nutrition;
        init => this.nutrition = value;
    }

    private float saturationModifier;

    public required float SaturationModifier
    {
        get => this.saturationModifier;
        init => this.saturationModifier = value;
    }

    private bool canAlwaysEat;

    public required bool CanAlwaysEat
    {
        get => this.canAlwaysEat;
        init => this.canAlwaysEat = value;
    }

    [SetsRequiredMembers]
    internal FoodDataComponent() { }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.nutrition = reader.ReadVarInt();
        this.saturationModifier = reader.ReadSingle();
        this.canAlwaysEat = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Nutrition);
        writer.WriteSingle(this.SaturationModifier);
        writer.WriteBoolean(this.CanAlwaysEat);
    }
}


