using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Makes the item consumable.
/// </summary>
public sealed record class FoodDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.nutrition,
        this.saturationModifier,
        this.canAlwaysEat
    });

    /// <inheritdoc/>
    public bool Equals(FoodDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Food;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:food";

    private int nutrition;

    /// <summary>
    /// Gets how much hunger eating the item restores, in half drumsticks.
    /// </summary>
    public required int Nutrition
    {
        get => this.nutrition;
        init => this.nutrition = value;
    }

    private float saturationModifier;

    /// <summary>
    /// Gets the saturation eating the item adds, capped at the eater's food level (vanilla's <c>saturation</c>). It's
    /// added as it is, not multiplied by <see cref="Nutrition"/>.
    /// </summary>
    public required float SaturationModifier
    {
        get => this.saturationModifier;
        init => this.saturationModifier = value;
    }

    private bool canAlwaysEat;

    /// <summary>
    /// Gets whether the item can be eaten without being hungry.
    /// </summary>
    public required bool CanAlwaysEat
    {
        get => this.canAlwaysEat;
        init => this.canAlwaysEat = value;
    }

    [SetsRequiredMembers]
    internal FoodDataComponent() { }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.nutrition = reader.ReadVarInt();
        this.saturationModifier = reader.ReadSingle();
        this.canAlwaysEat = reader.ReadBoolean();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Nutrition);
        writer.WriteSingle(this.SaturationModifier);
        writer.WriteBoolean(this.CanAlwaysEat);
    }
}


