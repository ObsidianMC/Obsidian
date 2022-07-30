namespace Obsidian.API.Events;

/// <summary>
/// Interface containing information related to the player.
/// </summary>
public interface IPlayerEvent
{
    /// <summary>
    /// Player related to the event.
    /// </summary>
    IPlayer Player { get; }
}
