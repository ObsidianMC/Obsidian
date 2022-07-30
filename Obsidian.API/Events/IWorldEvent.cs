namespace Obsidian.API.Events;

/// <summary>
/// Interface containing information related to the world.
/// </summary>
public interface IWorldEvent
{
    /// <summary>
    /// World the event is referring to
    /// </summary>
    IWorld World { get; }
}
