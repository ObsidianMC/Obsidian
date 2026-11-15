namespace Obsidian.API.Events;

/// <summary>
/// Provides a mechanism for cancelling default behaviour.
/// </summary>
public interface ICancellable
{
    /// <summary>
    /// Gets a value indicating whether the default behaviour should be cancelled.
    /// </summary>
    public bool IsCancelled { get; }

    /// <summary>
    /// Sets a flag which prevents default (vanilla) behavior from executing.
    /// </summary>
    /// <remarks>
    /// This flag only affects vanilla behavior: other handlers still run. To tell them the event was dealt with,
    /// see <see cref="AsyncEventArgs.SetHandled"/>.
    /// </remarks>
    public void Cancel();
}
