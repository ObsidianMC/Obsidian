namespace Obsidian.API.Events;

/// <summary>
/// Interface containing information related to the server.
/// </summary>
public interface IServerEvent
{
    /// <summary>
    /// Server which the event refers to.
    /// </summary>
    IServer Server { get; }
}
