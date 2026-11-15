namespace Obsidian.API.Effects;
/// <summary>A consume effect paired with its type id.</summary>
public readonly record struct ConsumeEffect
{
    /// <summary>Gets the effect's type id, such as <c>minecraft:apply_effects</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the effect's data.</summary>
    public required IConsumeEffect Effect { get; init; }
}
