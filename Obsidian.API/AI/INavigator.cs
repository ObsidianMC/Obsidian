namespace Obsidian.API.AI;

/// <summary>
/// Exposes an entity's requested navigation destination and current navigation status.
/// </summary>
public interface INavigator
{
    /// <summary>
    /// Gets whether navigation is currently active.
    /// </summary>
    public bool IsNavigating { get; }
    /// <summary>
    /// Gets whether navigation updates are paused.
    /// </summary>
    public bool IsPaused { get; }

    /// <summary>
    /// Gets whether the destination is an entity or a fixed world position.
    /// </summary>
    public TargetType TargetType { get; }

    /// <summary>
    /// Gets or sets the destination in world block coordinates.
    /// </summary>
    public VectorD TargetLocation { get; set; }

    /// <summary>
    /// Gets or sets the entity to follow, or <c>null</c> for a fixed destination.
    /// </summary>
    public IEntity? Target { get; set; }

    /// <summary>
    /// Requests navigation to a fixed world position, replacing the previous destination.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A destination component is not finite.</exception>
    public void NavigateTo(VectorD to);

    /// <summary>
    /// Requests navigation following an entity's position, replacing the previous destination.
    /// </summary>
    /// <exception cref="ArgumentNullException">The target entity is <c>null</c>.</exception>
    public void NavigateTo(IEntity to);
}

/// <summary>
/// Identifies how a navigation destination is supplied.
/// </summary>
public enum TargetType
{
    /// <summary>
    /// Follows an entity's changing world position.
    /// </summary>
    Entity,

    /// <summary>
    /// Navigates toward a fixed world position.
    /// </summary>
    Location
}
