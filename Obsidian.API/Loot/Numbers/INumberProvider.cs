namespace Obsidian.API.Loot.Numbers;

/// <summary>
/// A number drawn while generating loot, like vanilla's <c>NumberProvider</c>. In loot table JSON a plain number is a
/// <see cref="ConstantNumber"/>.
/// </summary>
public interface INumberProvider
{
    public float GetFloat(LootContext context);

    /// <summary>
    /// Vanilla's default rounds <see cref="GetFloat"/> half up (Java's <c>Math.round</c>).
    /// </summary>
    public int GetInt(LootContext context) => (int)Math.Floor(this.GetFloat(context) + 0.5d);
}
