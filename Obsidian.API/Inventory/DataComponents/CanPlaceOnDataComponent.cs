namespace Obsidian.API.Inventory.DataComponents;
public sealed record class CanPlaceOnDataComponent : BlockPredicatesDataComponent
{
    public override DataComponentType Type => DataComponentType.CanPlaceOn;

    public override string Identifier => "minecraft:can_place_on";
}

public sealed record class CanBreakDataComponent : BlockPredicatesDataComponent
{
    public override DataComponentType Type => DataComponentType.CanPlaceOn;

    public override string Identifier => "minecraft:can_break";
}
