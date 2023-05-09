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
    /// <remarks>This flag only affects vanilla behavior. If you wish to prevent other handlers from running, see <see cref="AsyncEventArgs.IsHandled"/>.</remarks>
    public void Cancel();
}
