using Obsidian.API.Inventory;
using Obsidian.API.Loot.Conditions;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Modifies a generated item stack, like vanilla's <c>LootItemConditionalFunction</c>. Functions run in order and each
/// receives the stack the previous one returned.
/// </summary>
public abstract class LootFunction
{
    /// <summary>
    /// The function only runs when all of these pass.
    /// </summary>
    public ILootCondition[] Conditions { get; init; } = [];

    /// <summary>
    /// Runs the function if its conditions pass and returns the resulting stack, which may be a new instance.
    /// </summary>
    public ItemStack Apply(ItemStack stack, LootContext context) =>
        ILootCondition.TestAll(this.Conditions, context) ? this.Run(stack, context) : stack;

    protected abstract ItemStack Run(ItemStack stack, LootContext context);

    /// <summary>
    /// Applies <paramref name="functions"/> in order.
    /// </summary>
    public static ItemStack ApplyAll(LootFunction[] functions, ItemStack stack, LootContext context)
    {
        foreach (var function in functions)
            stack = function.Apply(stack, context);

        return stack;
    }

    /// <summary>
    /// Wraps <paramref name="output"/> so every stack passes through <paramref name="functions"/> first.
    /// </summary>
    public static Action<ItemStack> Decorate(LootFunction[] functions, Action<ItemStack> output, LootContext context) =>
        functions.Length == 0 ? output : stack => output(ApplyAll(functions, stack, context));
}
