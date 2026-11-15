namespace Obsidian.API;

/// <summary>
/// The position of the scoreboard. 
/// 0: list, 
/// 1: sidebar, 
/// 2: below name,
/// 3 - 18: team specific sidebar, indexed as 3 + team color.
/// </summary>
public enum DisplaySlot : int
{
    /// <summary>The player list shown when holding Tab.</summary>
    List,

    /// <summary>The sidebar on the right of the screen.</summary>
    Sidebar,

    /// <summary>Under players' name tags.</summary>
    BelowName,

    /// <summary>The sidebar, shown only to players on a black team.</summary>
    TeamBlack,
    /// <summary>The sidebar, shown only to players on a dark blue team.</summary>
    TeamDarkBlue,
    /// <summary>The sidebar, shown only to players on a dark green team.</summary>
    TeamDarkGreen,
    /// <summary>The sidebar, shown only to players on a dark aqua team.</summary>
    TeamDarkAqua,
    /// <summary>The sidebar, shown only to players on a dark red team.</summary>
    TeamDarkRed,
    /// <summary>The sidebar, shown only to players on a dark purple team.</summary>
    TeamDarkPurple,
    /// <summary>The sidebar, shown only to players on a gold team.</summary>
    TeamGold,
    /// <summary>The sidebar, shown only to players on a gray team.</summary>
    TeamGray,
    /// <summary>The sidebar, shown only to players on a dark gray team.</summary>
    TeamDarkGray,
    /// <summary>The sidebar, shown only to players on a blue team.</summary>
    TeamBlue,
    /// <summary>The sidebar, shown only to players on a green team.</summary>
    TeamGreen,
    /// <summary>The sidebar, shown only to players on an aqua team.</summary>
    TeamAqua,
    /// <summary>The sidebar, shown only to players on a red team.</summary>
    TeamRed,
    /// <summary>The sidebar, shown only to players on a light purple team.</summary>
    TeamLightPurple,
    /// <summary>The sidebar, shown only to players on a yellow team.</summary>
    TeamYellow,
    /// <summary>The sidebar, shown only to players on a white team.</summary>
    TeamWhite
}
