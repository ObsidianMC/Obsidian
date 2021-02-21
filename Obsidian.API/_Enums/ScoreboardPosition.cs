namespace Obsidian.API
{
    //	The position of the scoreboard. 0: list, 1: sidebar, 2: below name, 3 - 18: team specific sidebar, indexed as 3 + team color.
    public enum ScoreboardPosition : sbyte
    {
        List,

        Sidebar,

        BelowName
    }
}
