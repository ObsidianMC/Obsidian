namespace Obsidian.API;

/// <summary>Flags a client sends with its movement packets.</summary>
[Flags]
public enum MovementFlags
{
    /// <summary>No flags set.</summary>
    None,
    /// <summary>The entity is standing on the ground.</summary>
    OnGround,
    /// <summary>The entity is pushing against a wall.</summary>
    HorizontalCollision
}
