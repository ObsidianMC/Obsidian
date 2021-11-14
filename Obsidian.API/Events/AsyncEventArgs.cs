using System;

namespace Obsidian.API.Events;

/// <summary>
/// Represents the base class for classes that contain event data, where events are invoked asynchronously.
/// </summary>
public abstract class AsyncEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets whether the event was completely handled. Setting this to true will prevent remaining handlers from running.
    /// </summary>
    public bool Handled { get; set; }
}
