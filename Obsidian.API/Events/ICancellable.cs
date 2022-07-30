namespace Obsidian.API.Events;

/// <summary>
/// Provides a mechanism for cancelling default behaviour.
/// </summary>
public interface ICancellable
{
    /// <summary>
    /// Value indicating whether the default behaviour should be cancelled.
    /// </summary>
    bool Cancelled { get; set; }
}
