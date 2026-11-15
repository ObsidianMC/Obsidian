namespace Obsidian.API.Loot.Numbers;

/// <summary>
/// A number provider that always gives <see cref="Value"/> (vanilla's <c>minecraft:constant</c>).
/// </summary>
[LootType("minecraft:constant")]
public sealed class ConstantNumber : INumberProvider
{
    /// <summary>
    /// Gets the value the provider gives.
    /// </summary>
    public required float Value { get; init; }

    /// <inheritdoc/>
    public float GetFloat(LootContext context) => this.Value;
}
