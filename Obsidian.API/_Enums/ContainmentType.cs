namespace Obsidian.API;

/// <summary>How one shape relates to another it is tested against.</summary>
public enum ContainmentType
{
    /// <summary>The shapes neither overlap nor touch.</summary>
    Disjoint,
    /// <summary>The tested shape lies fully inside the other.</summary>
    Contains,
    /// <summary>The shapes overlap or touch, but the tested shape is not fully inside the other.</summary>
    Intersects
}
