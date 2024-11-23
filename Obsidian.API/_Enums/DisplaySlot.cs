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
    List,

    Sidebar,

    BelowName,

    TeamBlack,
    TeamDarkBlue,
    TeamDarkGreen,
    TeamDarkAqua,
    TeamDarkRed,
    TeamDarkPurple,
    TeamGold,
    TeamGray,
    TeamDarkGray,
    TeamBlue,
    TeamGreen,
    TeamAqua,
    TeamRed,
    TeamLightPurple,
    TeamYellow,
    TeamWhite
}
