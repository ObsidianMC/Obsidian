namespace Obsidian.API.Inventory.DataComponents;
public sealed class CanPlaceOnDataComponent : BlockPredicatesDataComponent
{
    public override DataComponentType Type => DataComponentType.CanPlaceOn;

    public override string Identifier => "minecraft:can_place_on";
}

public sealed class CanBreakDataComponent : BlockPredicatesDataComponent
{
    public override DataComponentType Type => DataComponentType.CanPlaceOn;

    public override string Identifier => "minecraft:can_break";
}
