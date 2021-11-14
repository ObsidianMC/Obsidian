using System;
using System.Collections.Generic;

namespace Obsidian.API.Advancements;

/// <summary>
/// Criteria for advancements.
/// If this is a custom critera developers will have to track the players themselves to ensure they achieve the required criteria
/// </summary>
public class Criteria
{
    public string Identifier { get; }

    public bool Required { get; }

    public string? Trigger { get; init; }

    public bool Achieved { get; private set; }

    public DateTimeOffset? AchievedAt { get; private set; }

    //TODO
    //public IReadOnlyDictionary<string, INbtTag> Conditions { get; private set; } = new();

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
