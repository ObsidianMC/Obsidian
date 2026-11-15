namespace Obsidian.API;

/// <summary>Who can see the name tags of a team's members.</summary>
public enum NameTagVisibility
{
    /// <summary>Everyone sees them.</summary>
    Always,

    /// <summary>Only members of the same team see them.</summary>
    HideForOtherTeams,
    /// <summary>Only players not on the team see them.</summary>
    HideForOwnTeam,

    /// <summary>No one sees them.</summary>
    Never
}
