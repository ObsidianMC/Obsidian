namespace Obsidian.API.Advancements;

/// <summary>
/// Criteria for advancements.
/// Runtime progress belongs to each player's IAdvancements; these legacy fields are only for callers building packets.
/// </summary>
public class Criteria
{
    public string Identifier { get; }

    public bool Required { get; }

    public string? Trigger { get; init; }

    public bool Achieved { get; private set; }

    public DateTimeOffset? AchievedAt { get; private set; }

    /// <summary>Detached vanilla-shaped conditions, evaluated by the registered trigger.</summary>
    public System.Text.Json.JsonElement Conditions { get; init; }

    public Criteria(string name, bool required)
    {
        this.Identifier = name;
        this.Required = required;
    }

    public void CompleteCriteria(DateTimeOffset completedAt)
    {
        this.AchievedAt = completedAt;
        this.Achieved = true;
    }
}
