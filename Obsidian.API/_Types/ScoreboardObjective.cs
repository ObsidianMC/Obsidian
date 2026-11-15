namespace Obsidian.API;

/// <summary>A scoreboard objective: its name, the title players see, and how its scores are shown.</summary>
public sealed class ScoreboardObjective
{
    /// <summary>Creates an objective.</summary>
    /// <param name="objectiveName">The objective's internal name.</param>
    /// <param name="value">The title players see.</param>
    /// <param name="displayType">How scores are shown.</param>
    public ScoreboardObjective(string objectiveName, ChatMessage value, DisplayType displayType)
    {
        ObjectiveName = objectiveName;
        Value = value;
        DisplayType = displayType;
    }

    /// <summary>Gets the objective's internal name.</summary>
    public string ObjectiveName { get; internal set; }

    /// <summary>Gets the title players see.</summary>
    public ChatMessage Value { get; internal set; }

    /// <summary>Gets how the objective's scores are shown.</summary>
    public DisplayType DisplayType { get; internal set; }
}
