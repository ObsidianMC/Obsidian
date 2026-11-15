namespace Obsidian.API;

/// <summary>A score shown for one score holder on a scoreboard objective.</summary>
public sealed class Score
{
    /// <summary>Creates a score.</summary>
    /// <param name="displayText">The name shown for the score holder.</param>
    /// <param name="value">The score.</param>
    public Score(string displayText, int value)
    {
        DisplayText = displayText;
        Value = value;
    }

    /// <summary>Gets the name shown for the score holder.</summary>
    public string DisplayText { get; internal set; }

    /// <summary>Gets the score.</summary>
    public int Value { get; internal set; }
}
