namespace Obsidian.API;
/// <summary>The outcome of running an event's handlers.</summary>
public enum EventResult
{
    /// <summary>The handlers ran and one of them cancelled the event.</summary>
    Cancelled,

    /// <summary>The handlers ran and none cancelled the event, or no handler is registered.</summary>
    Completed,

    /// <summary>A handler threw an exception, so the rest were not run.</summary>
    Failed
}
