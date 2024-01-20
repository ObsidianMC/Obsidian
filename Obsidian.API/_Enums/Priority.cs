using System.ComponentModel;

namespace Obsidian.API;

public enum Priority
{
    /// <summary>
    /// Event must be called first as it is not important.
    /// </summary>
    Low,

    /// <summary>
    /// The event should be ran normally.
    /// </summary>
    Normal,

    /// <summary>
    /// The event is important
    /// </summary>
    High,

    /// <summary>
    /// The event is extremely important and should be ran last.
    /// </summary>
    Critical,

    /// <summary>
    /// Only used for internal vanilla events and specifies the event should always be called last.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    Internal
}
