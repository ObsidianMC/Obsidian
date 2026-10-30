namespace Obsidian.API.Loot.Numbers;

/// <summary>
/// The programmatic plugin loot number API. Loot JSON uses the shared typed context providers through
/// <see cref="LootNumberProviders"/>; existing plugins can still construct <see cref="ConstantNumber"/> values.
/// </summary>
public interface INumberProvider
{
    public float GetFloat(LootContext context);

    /// <summary>
    /// The compatibility default rounds <see cref="GetFloat"/> half up (Java's <c>Math.round</c>).
    /// </summary>
    public int GetInt(LootContext context) => (int)Math.Floor(this.GetFloat(context) + 0.5d);
}
