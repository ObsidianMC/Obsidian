namespace Obsidian.API.Utilities.Interfaces;
public interface IEventExecutor : IExecutor
{
    public Type EventType { get; init; }

    public Priority Priority { get; init; }
}
