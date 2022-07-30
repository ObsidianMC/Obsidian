namespace Obsidian.API.Events;

/// <summary>
/// Interface containing information related to the event.
/// </summary>
public interface IEntityEvent
{
    /// <summary>
    /// Entity related to the event.
    /// </summary>
    IEntity Entity { get; }
}
