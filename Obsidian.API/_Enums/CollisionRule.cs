namespace Obsidian.API;

/// <summary>Which entities a team's members push and are pushed by.</summary>
public enum CollisionRule
{
    /// <summary>Members push and are pushed by every entity.</summary>
    Always,

    /// <summary>Members only push and are pushed by entities that are not on their team.</summary>
    PushOtherTeams,
    /// <summary>Members only push and are pushed by entities on their own team.</summary>
    PushOwnTeam,

    /// <summary>Members never push or get pushed by other entities.</summary>
    Never
}
