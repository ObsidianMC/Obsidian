using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Crafting;

/// <summary>
/// A brewing stand recipe (<c>minecraft:brewing</c>, 26.3): <see cref="Reagent"/> in the ingredient slot brews each
/// bottle <see cref="Input"/> matches into a new <see cref="Output"/>.
/// </summary>
public sealed class BrewingRecipe : IRecipe
{
    public required string Identifier { get; init; }

    public required CraftingType Type { get; init; }

    public string? Group { get; init; }

    /// <summary>
    /// The bottles the recipe brews.
    /// </summary>
    public required PotionIngredient Input { get; init; }

    /// <summary>
    /// What goes in the stand's ingredient slot.
    /// </summary>
    public required PotionIngredient Reagent { get; init; }

    /// <summary>
    /// What each bottle becomes. Like vanilla's, it's a new stack: nothing of the bottle it replaces carries over.
    /// </summary>
    public required ItemStack Output { get; init; }

    internal BrewingRecipe() { }

    /// <summary>
    /// Whether the recipe brews <paramref name="bottle"/> with <paramref name="reagent"/> (vanilla's
    /// <c>BrewingRecipe.matches</c>).
    /// </summary>
    public bool Matches(ItemStack bottle, ItemStack reagent) =>
        this.Input.Matches(bottle) && this.Reagent.Matches(reagent);
}

/// <summary>
/// One side of a <see cref="BrewingRecipe"/> (vanilla's <c>PotionIngredient</c>): one of <see cref="Items"/>, holding
/// one of <see cref="Potions"/> when that's set.
/// </summary>
/// <param name="Items">The items that match.</param>
/// <param name="Potions">
/// The potions a matching item holds, or null to match any item of <paramref name="Items"/>.
/// </param>
public sealed record PotionIngredient(Ingredient Items, IReadOnlyList<Potion>? Potions)
{
    /// <summary>
    /// Whether <paramref name="item"/> matches: it's one of the items, and holds one of the potions when they're set.
    /// Only the item and its potion count, not its other components.
    /// </summary>
    public bool Matches(ItemStack item)
    {
        if (item.IsAir || !this.Items.Any(entry => entry.Holder == item.Holder))
            return false;

        if (this.Potions is null)
            return true;

        var contents = item.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        return contents?.Potion is Potion potion && this.Potions.Contains(potion);
    }
}
