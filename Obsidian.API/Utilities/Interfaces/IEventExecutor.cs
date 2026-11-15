namespace Obsidian.API.Utilities.Interfaces;
/// <summary>
/// Describes an executable event handler and its dispatch priority.
/// </summary>
public interface IEventExecutor : IExecutor
{
    /// <summary>
    /// Gets the type of event handled by this executor.
    /// </summary>
    public Type EventType { get; init; }

    /// <summary>
    /// Gets the priority used to order this handler during event dispatch.
    /// </summary>
    public Priority Priority { get; init; }
}
