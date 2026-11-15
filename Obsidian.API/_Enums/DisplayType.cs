namespace Obsidian.API;

/// <summary>How the client draws a scoreboard objective's scores in the player list.</summary>
public enum DisplayType : int
{
    /// <summary>Scores are shown as numbers.</summary>
    Integer,

    /// <summary>Scores are shown as hearts.</summary>
    Hearts
}
