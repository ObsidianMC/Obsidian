using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;

namespace Obsidian.API.Inventory;

/// <summary>
/// Vanilla's <c>ItemStack.validateStrict</c>, which a recipe's result template and a loot table's
/// <c>set_components</c> check a stack with.
/// </summary>
internal static class ItemStackValidation
{
    /// <summary>
    /// Whether <paramref name="stack"/> is valid: no more items than its stack size, not both damageable and
    /// stackable, and no stack it holds (in a container, bundle or loaded crossbow) larger than that stack's size.
    /// Vanilla's bundle weight check fails only when the weight overflows, which no valid bundle reaches.
    /// </summary>
    public static bool IsValid(ItemStack stack)
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

    private static bool HeldStacksFit(ItemStack stack, DataComponentType type)
    {
        var held = stack.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type)?.Value ?? [];

        return held
            .Where(item => !item.IsNullOrAir() && item.Count > 0)
            .All(item => item.Count <= MaxStackSize(item));
    }

    // Vanilla's getMaxStackSize, whose default is 1: a stack whose max_stack_size component was removed holds one.
    private static int MaxStackSize(ItemStack stack) =>
        stack.RemoveComponents.Contains(DataComponentType.MaxStackSize) ? 1 : stack.MaxStackSize;
}
