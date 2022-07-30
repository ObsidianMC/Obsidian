namespace Obsidian.API.Events;

/// <summary>
/// Base EventArgs for all events.
/// </summary>
public class BaseEventArgs : EventArgs
{
    /// <summary>
    /// Whether the event was completely handled.
    /// </summary>
    public bool Handled { get; private set; }

    /// <summary>
    /// Marks the event as handled.
    /// </summary>
    public void Handle()
    {
        Handled = true;
    }
}
