namespace Obsidian.API.Events;

/// <summary>
/// Provides a mechanism for cancelling default behaviour.
/// </summary>
public interface ICancellable
{
    /// <summary>
    /// Gets or sets a value indicating whether the default behaviour should be cancelled.
    /// </summary>
    public bool Cancel { get; set; }
}
