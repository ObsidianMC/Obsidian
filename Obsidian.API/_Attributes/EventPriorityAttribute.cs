namespace Obsidian.API;

/// <summary>Marks an event handler and selects its order within a dispatch.</summary>
/// <remarks>
/// Priority does not serialize separate dispatches. Handlers can run concurrently on different threads and must
/// make state shared across events thread-safe; see <see cref="IEventDispatcher"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EventPriorityAttribute : Attribute
{
    public Priority Priority { get; set; }
}
