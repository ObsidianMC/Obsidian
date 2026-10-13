namespace Obsidian.API.Loot;

/// <summary>
/// Thrown by loot constructs whose schema is loaded but whose evaluation needs block loot context that isn't wired yet.
/// Failing loudly keeps a missing feature from silently generating default results.
/// </summary>
internal static class LootPending
{
    public static NotSupportedException Evaluation(string type) =>
        new($"{type} is loaded but not evaluated yet: its block loot context isn't implemented.");
}
