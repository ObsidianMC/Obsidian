namespace Obsidian.API.Advancements;

/// <summary>
/// Criteria for advancements.
/// Runtime progress belongs to each player's IAdvancements; these legacy fields are only for callers building packets.
/// </summary>
public class Criteria
{
    /// <summary>Gets the criterion's name.</summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets whether the criterion is required on its own when its advancement has no
    /// <see cref="Advancement.Requirements"/>.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Gets the id of the trigger that checks this criterion, such as <c>minecraft:inventory_changed</c>.
    /// </summary>
    public string? Trigger { get; init; }

    /// <summary>Gets whether <see cref="CompleteCriteria"/> has been called.</summary>
    public bool Achieved { get; private set; }

    /// <summary>Gets when <see cref="CompleteCriteria"/> was called, or null.</summary>
    public DateTimeOffset? AchievedAt { get; private set; }

    /// <summary>Detached vanilla-shaped conditions, evaluated by the registered trigger.</summary>
    public System.Text.Json.JsonElement Conditions { get; init; }

    /// <summary>Creates a criterion.</summary>
    /// <param name="name">The criterion's name.</param>
    /// <param name="required">Whether it is required on its own when its advancement has no
    /// <see cref="Advancement.Requirements"/>.</param>
    public Criteria(string name, bool required)
    {
        this.Identifier = name;
        this.Required = required;
    }

    /// <summary>Marks the criterion as achieved at <paramref name="completedAt"/>.</summary>
    public void CompleteCriteria(DateTimeOffset completedAt)
    {
        this.AchievedAt = completedAt;
        this.Achieved = true;
    }
}
