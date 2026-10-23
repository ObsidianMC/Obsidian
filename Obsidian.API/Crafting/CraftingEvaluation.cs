using Obsidian.API.Inventory;

namespace Obsidian.API.Crafting;

/// <summary>
/// A detached evaluation of one craft. Slot lists use the input grid's row-major, zero-based indexes.
/// Consumed counts describe one craft, and remainders are the items left by those consumed ingredients.
/// Compare RecipeRevision with IServer.CraftingRecipeRevision before committing a cached evaluation.
/// </summary>
public sealed record CraftingEvaluation(
    ItemStack Output,
    string RecipeIdentifier,
    IReadOnlyList<int> ConsumedCounts,
    IReadOnlyList<ItemStack?> Remainders,
    long RecipeRevision);
