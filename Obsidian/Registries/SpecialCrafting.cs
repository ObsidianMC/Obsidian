using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.WorldData;

namespace Obsidian.Registries;

/// <summary>
/// Matches 26.1's configurable special crafting recipes (<see cref="DyeRecipe"/>, <see cref="ImbueRecipe"/> and
/// <see cref="TransmuteRecipe"/>) like vanilla's recipes of those types. What they craft depends on the grid's items,
/// so matching a grid assembles its result too.
/// </summary>
internal static class SpecialCrafting
{
    /// <param name="grid">The grid's slots, row by row; null slots are empty.</param>
    /// <param name="width">The grid's width, 2 or 3.</param>
    /// <returns>A new stack of what the recipe crafts from the grid's items, or <c>null</c> if it doesn't.</returns>
    public static ItemStack? Assemble(ICraftingRecipe recipe, IReadOnlyList<ItemStack?> grid, int width)
    {
        return recipe switch
        {
            DyeRecipe dye => Dye(dye, Occupied(grid)),
            ImbueRecipe imbue => Imbue(imbue, grid, width),
            TransmuteRecipe transmute => Transmute(transmute, Occupied(grid)),
            _ => null
        };
    }

    // One target and at least one dye item, in any slots.
    private static ItemStack? Dye(DyeRecipe recipe, List<ItemStack> items)
    {
        ItemStack? target = null;
        var colors = new List<Dye>();

        foreach (var item in items)
        {
            if (Matches(recipe.Target, item))
            {
                if (target is not null)
                    return null;

                target = item;
            }
            else if (Matches(recipe.Dye, item) && ItemDyes.Of(item) is Dye color)
            {
                colors.Add(color);
            }
            else
            {
                return null;
            }
        }

        if (target is null || colors.Count == 0)
            return null;

        var previous = target.GetComponent<SimpleDataComponent<int>>(DataComponentType.DyedColor)?.Value;
        var result = WithComponentsOf(target, recipe.Result.Holder, recipe.Result.Count);
        result[DataComponentType.DyedColor] = ComponentBuilder.DyedColor with { Value = MixDyes(previous, colors) };

        return result;
    }

    /// <summary>
    /// The color dyeing an item of color <paramref name="previous"/>, if any, with <paramref name="dyes"/> gives it,
    /// like vanilla's <c>DyedItemColor.applyDyes</c>: the average of the colors, brightened to their average
    /// brightness (each color's brightest channel).
    /// </summary>
    internal static int MixDyes(int? previous, IReadOnlyList<Dye> dyes)
    {
        var colors = dyes.Select(ItemDyes.TextureColor).ToList();
        if (previous is int color)
            colors.Insert(0, color);

        int red = colors.Sum(Red) / colors.Count;
        int green = colors.Sum(Green) / colors.Count;
        int blue = colors.Sum(Blue) / colors.Count;

        // Vanilla works these out in single precision.
        float brightness = (float)colors.Sum(rgb => Math.Max(Red(rgb), Math.Max(Green(rgb), Blue(rgb)))) / colors.Count;
        float brightest = Math.Max(red, Math.Max(green, blue));

        int mixedRed = (int)(red * brightness / brightest);
        int mixedGreen = (int)(green * brightness / brightest);
        int mixedBlue = (int)(blue * brightness / brightest);

        return mixedRed << 16 | mixedGreen << 8 | mixedBlue;
    }

    private static int Red(int rgb) => rgb >> 16 & 0xFF;

    private static int Green(int rgb) => rgb >> 8 & 0xFF;

    private static int Blue(int rgb) => rgb & 0xFF;

    // A full 3x3 grid: the source in the middle and the material around it.
    private static ItemStack? Imbue(ImbueRecipe recipe, IReadOnlyList<ItemStack?> grid, int width)
    {
        const int middle = 4;
        if (width != 3)
            return null;

        for (int slot = 0; slot < grid.Count; slot++)
        {
            var item = grid[slot];
            var ingredient = slot == middle ? recipe.Source : recipe.Material;
            if (!IsOccupied(item) || !Matches(ingredient, item!))
                return null;
        }

        // The source's potion: its own, or its item's default (no potion), which the result's item has too.
        var source = grid[middle]!;
        var result = new ItemStack(recipe.Result.Holder, recipe.Result.Count);
        var contents = source.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        if (contents is not null)
            result[DataComponentType.PotionContents] = contents;
        else if (source.RemoveComponents.Contains(DataComponentType.PotionContents))
            result.Remove(DataComponentType.PotionContents);

        return result;
    }

    // One input and MinMaterialCount to MaxMaterialCount materials, in any slots. A recipe that would make one of
    // the same input back, as dyeing a red shulker box red would, doesn't match.
    private static ItemStack? Transmute(TransmuteRecipe recipe, List<ItemStack> items)
    {
        ItemStack? input = null;
        int materials = 0;

        foreach (var item in items)
        {
            if (Matches(recipe.Input, item))
            {
                if (input is not null)
                    return null;

                input = item;
            }
            else if (Matches(recipe.Material, item))
            {
                materials++;
            }
            else
            {
                return null;
            }
        }

        if (input is null || materials < recipe.MinMaterialCount || materials > recipe.MaxMaterialCount)
            return null;

        var count = recipe.AddMaterialCountToResult ? recipe.ResultCount + materials : recipe.ResultCount;
        var result = WithComponentsOf(input, recipe.ResultItem ?? input.Holder, count);
        if (count == 1 && result == input)
            return null;

        return result;
    }

    // A stack of item with the components set on (or removed from) the source, like vanilla's
    // TransmuteRecipe.createWithOriginalComponents.
    private static ItemStack WithComponentsOf(ItemStack source, Item item, int count)
    {
        var result = source.TransmuteCopy(item);
        result.Count = count;

        return result;
    }

    // The grid's items, row by row.
    private static List<ItemStack> Occupied(IReadOnlyList<ItemStack?> grid) => grid
        .Where(IsOccupied)
        .Select(item => item!)
        .ToList();

    private static bool IsOccupied(ItemStack? item) => !item.IsNullOrAir() && item!.Count > 0;

    private static bool Matches(Ingredient ingredient, ItemStack item) =>
        ingredient.Any(candidate => candidate.Holder == item.Holder);
}
