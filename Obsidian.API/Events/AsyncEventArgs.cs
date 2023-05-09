namespace Obsidian.API.Events;

/// <summary>
/// Represents the base class for classes that contain event data, where events are invoked asynchronously.
/// </summary>
public abstract class AsyncEventArgs : EventArgs
{
    /// <summary>
    /// Gets a value indicating whether the event was completely handled.
    /// </summary>
    public bool Handled { get; private set; }

    /// <summary>
    /// Marks an event as handled. Setting this flag will prevent remaining handlers from running.
    /// </summary>
    public void SetHandled()
    {
        Handled = true;
    }
}
