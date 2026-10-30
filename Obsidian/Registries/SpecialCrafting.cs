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
    /// <summary>
    /// Matches <paramref name="recipe"/> against a grid and assembles what it crafts.
    /// </summary>
    /// <param name="grid">The grid's slots, row by row; null slots are empty.</param>
    /// <param name="width">The grid's width, 2 or 3.</param>
    /// <param name="result">
    /// A new stack of what the recipe crafts, or <c>null</c> when it doesn't match or, like vanilla's empty result,
    /// when what it assembles isn't a valid stack.
    /// </param>
    /// <returns>Whether the recipe matches, which ends the search for a recipe even when it crafts nothing.</returns>
    public static bool TryAssemble(
        ICraftingRecipe recipe,
        IReadOnlyList<ItemStack?> grid,
        int width,
        out ItemStack? result)
    {
        result = null;

        return recipe switch
        {
            DyeRecipe dye => TryDye(dye, Occupied(grid), out result),
            ImbueRecipe imbue => TryImbue(imbue, grid, width, out result),
            TransmuteRecipe transmute => TryTransmute(transmute, Occupied(grid), out result),
            _ => false
        };
    }

    /// <summary>
    /// Whether <paramref name="stack"/> is valid by vanilla's <c>ItemStack.validateStrict</c>, which a recipe's
    /// result template checks the result with: no more items than its stack size, not both damageable and
    /// stackable, and no stack it holds (in a container, bundle or loaded crossbow) larger than that stack's size.
    /// Vanilla's bundle weight check fails only when the weight overflows, which no valid bundle reaches.
    /// </summary>
    internal static bool IsValidStack(ItemStack stack)
    {
        var maxStackSize = MaxStackSize(stack);
        if (stack.MaxDamage > 0 && maxStackSize > 1)
            return false;

        if (stack.Count > maxStackSize)
            return false;

        return HeldStacksFit(stack, DataComponentType.Container)
            && HeldStacksFit(stack, DataComponentType.BundleContents)
            && HeldStacksFit(stack, DataComponentType.ChargedProjectiles);
    }

    // One target and at least one dye item, in any slots.
    private static bool TryDye(DyeRecipe recipe, List<ItemStack> items, out ItemStack? result)
    {
        result = null;
        ItemStack? target = null;
        var colors = new List<Dye>();

        foreach (var item in items)
        {
            if (Matches(recipe.Target, item))
            {
                if (target is not null)
                    return false;

                target = item;
            }
            else if (Matches(recipe.Dye, item) && ItemDyes.Of(item) is Dye color)
            {
                colors.Add(color);
            }
            else
            {
                return false;
            }
        }

        if (target is null || colors.Count == 0)
            return false;

        // Like vanilla, the result is checked before it's dyed, which doesn't change whether it's valid.
        result = FromTemplate(recipe.Result, target);
        if (result is null)
            return true;

        var previous = target.GetComponent<SimpleDataComponent<int>>(DataComponentType.DyedColor)?.Value;
        result[DataComponentType.DyedColor] = ComponentBuilder.DyedColor with { Value = MixDyes(previous, colors) };

        return true;
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
    private static bool TryImbue(ImbueRecipe recipe, IReadOnlyList<ItemStack?> grid, int width, out ItemStack? result)
    {
        const int middle = 4;
        result = null;
        if (width != 3)
            return false;

        for (int slot = 0; slot < grid.Count; slot++)
        {
            var item = grid[slot];
            var ingredient = slot == middle ? recipe.Source : recipe.Material;
            if (!IsOccupied(item) || !Matches(ingredient, item!))
                return false;
        }

        // Like vanilla, the result is checked before it takes the potion, which doesn't change whether it's valid.
        result = Validated(WithoutPotion(recipe.Result));
        if (result is null)
            return true;

        // The source's potion, replacing any the template sets: its own, or its item's default (no potion), which
        // the result's item has too.
        var source = grid[middle]!;
        var contents = source.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        if (contents is not null)
            result[DataComponentType.PotionContents] = contents;
        else if (source.RemoveComponents.Contains(DataComponentType.PotionContents))
            result.Remove(DataComponentType.PotionContents);

        return true;
    }

    // A stack of the template without the potion it sets or removes.
    private static ItemStack WithoutPotion(ItemStack template)
    {
        var components = template.Patch.Where(component => component.Type != DataComponentType.PotionContents);
        var removed = template.RemoveComponents.Where(type => type != DataComponentType.PotionContents);
        var stack = new ItemStack(template.Holder, template.Count, components);

        foreach (var type in removed)
            stack.Remove(type);

        return stack;
    }

    // One input and MinMaterialCount to MaxMaterialCount materials, in any slots.
    private static bool TryTransmute(TransmuteRecipe recipe, List<ItemStack> items, out ItemStack? result)
    {
        result = null;
        ItemStack? input = null;
        int materials = 0;

        foreach (var item in items)
        {
            if (Matches(recipe.Input, item))
            {
                if (input is not null)
                    return false;

                input = item;
            }
            else if (Matches(recipe.Material, item))
            {
                materials++;
            }
            else
            {
                return false;
            }
        }

        if (input is null || materials < recipe.MinMaterialCount || materials > recipe.MaxMaterialCount)
            return false;

        var count = recipe.AddMaterialCountToResult ? recipe.ResultCount + materials : recipe.ResultCount;
        var template = TransmuteTemplate(recipe, recipe.ResultItem ?? input.Holder, count);
        result = FromTemplate(template, input);

        // Vanilla's matching assembles a single result to compare it with the input: one that isn't a valid stack,
        // or that is the input again (as dyeing a red shulker box red would be), doesn't match. A larger result
        // matches whatever it assembles.
        if (count == 1 && (result is null || result == input))
        {
            result = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// A transmute recipe's result template for <paramref name="count"/> of <paramref name="item"/>: the components
    /// the recipe sets and removes, which the result applies over the input's (vanilla's <c>TransmuteResult</c>).
    /// </summary>
    internal static ItemStack TransmuteTemplate(TransmuteRecipe recipe, Item item, int count)
    {
        var template = new ItemStack(item, count, recipe.ResultComponents);

        foreach (var type in recipe.RemovedResultComponents)
            template.Remove(type);

        return template;
    }

    // A stack of the template's item and count with the components set on (or removed from) the source, then the
    // template's own over them, or null if that isn't a valid stack, like vanilla's
    // TransmuteRecipe.createWithOriginalComponents.
    private static ItemStack? FromTemplate(ItemStack template, ItemStack source)
    {
        var result = source.TransmuteCopy(template.Holder);
        result.Count = template.Count;

        foreach (var component in template.Patch)
            result[component.Type] = component;

        foreach (var type in template.RemoveComponents)
            result.Remove(type);

        return Validated(result);
    }

    // Like vanilla's ItemStackTemplate, which logs an invalid result and creates an empty stack instead.
    private static ItemStack? Validated(ItemStack stack) => IsValidStack(stack) ? stack : null;

    private static bool HeldStacksFit(ItemStack stack, DataComponentType type)
    {
        var held = stack.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type)?.Value ?? [];

        return held
            .Where(IsOccupied)
            .All(item => item.Count <= MaxStackSize(item));
    }

    // Vanilla's getMaxStackSize, whose default is 1: a stack whose max_stack_size component was removed holds one.
    private static int MaxStackSize(ItemStack stack) =>
        stack.RemoveComponents.Contains(DataComponentType.MaxStackSize) ? 1 : stack.MaxStackSize;

    // The grid's items, row by row.
    private static List<ItemStack> Occupied(IReadOnlyList<ItemStack?> grid) => grid
        .Where(IsOccupied)
        .Select(item => item!)
        .ToList();

    private static bool IsOccupied(ItemStack? item) => !item.IsNullOrAir() && item!.Count > 0;

    private static bool Matches(Ingredient ingredient, ItemStack item) =>
        ingredient.Any(candidate => candidate.Holder == item.Holder);
}
