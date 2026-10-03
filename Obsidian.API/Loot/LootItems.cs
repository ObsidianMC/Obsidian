using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Loot;

/// <summary>
/// Item stack and random helpers that follow vanilla's semantics where Obsidian's differ.
/// </summary>
internal static class LootItems
{
    /// <summary>
    /// The stack's item as vanilla's <c>ItemStack.getItem</c> sees it: a stack with no items left is air, so item
    /// checks on it fail.
    /// </summary>
    public static Item ItemOf(ItemStack stack) => IsEmpty(stack) ? ItemsRegistry.Air : stack.Holder;

    public static bool Is(ItemStack stack, Material material) => ItemOf(stack).Type == material;

    public static bool IsEmpty(ItemStack? stack) => stack is null || stack.IsAir || stack.Count <= 0;

    /// <summary>
    /// Sets <paramref name="component"/> on <paramref name="stack"/>, replacing a component of the same type.
    /// </summary>
    public static void Set(ItemStack stack, DataComponent component) => stack[component.Type] = component;

    /// <summary>
    /// Removes up to <paramref name="count"/> items from <paramref name="stack"/> and returns them as a new stack with
    /// the same components.
    /// </summary>
    public static ItemStack Split(ItemStack stack, int count)
    {
        var taken = Math.Min(count, stack.Count);
        var split = new ItemStack(stack, taken);
        stack.Count -= taken;
        return split;
    }

    /// <summary>
    /// Vanilla's <c>Mth.nextInt</c>: a value in <c>[min, max]</c>, drawing nothing when <paramref name="min"/> is at
    /// least <paramref name="max"/>.
    /// </summary>
    public static int NextInt(IRandomSource random, int min, int max) => min >= max ? min : random.NextInt(max - min + 1) + min;

    /// <summary>
    /// Vanilla's <c>Util.shuffle</c> (Fisher-Yates from the end).
    /// </summary>
    public static void Shuffle<T>(List<T> list, IRandomSource random)
    {
        for (var i = list.Count; i > 1; i--)
        {
            var swap = random.NextInt(i);
            (list[i - 1], list[swap]) = (list[swap], list[i - 1]);
        }
    }
}
