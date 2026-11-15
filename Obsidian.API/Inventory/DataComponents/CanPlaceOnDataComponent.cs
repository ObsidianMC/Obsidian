namespace Obsidian.API.Inventory.DataComponents;
/// <summary>
/// The blocks an item can be placed on in adventure mode (<c>minecraft:can_place_on</c>).
/// </summary>
public sealed record class CanPlaceOnDataComponent : BlockPredicatesDataComponent
{
    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.CanPlaceOn;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:can_place_on";
}

/// <summary>
/// The blocks an item can break in adventure mode (<c>minecraft:can_break</c>).
/// </summary>
public sealed record class CanBreakDataComponent : BlockPredicatesDataComponent
{
    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.CanBreak;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:can_break";
}
