namespace Obsidian.API.Loot.Conditions;

/// <summary>
/// A predicate that gates a pool, entry or function, like vanilla's <c>LootItemCondition</c>.
/// </summary>
public interface ILootCondition
{
    /// <summary>
    /// Whether the condition passes in <paramref name="context"/>. May draw from <see cref="LootContext.Random"/>.
    /// </summary>
    public bool Test(LootContext context);

    /// <summary>
    /// Tests every condition in order and stops at the first that fails, as vanilla does. Later conditions draw no
    /// random numbers once one fails.
    /// </summary>
    public static bool TestAll(ImmutableArray<ILootCondition> conditions, LootContext context)
    {
        foreach (var condition in conditions)
        {
            if (!condition.Test(context))
                return false;
        }

        return true;
    }
}
