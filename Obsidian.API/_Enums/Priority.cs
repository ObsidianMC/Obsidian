using System.ComponentModel;

namespace Obsidian.API;

//TODO add more
public enum Priority
{
    Low,

    Normal,

    High,

    Critical,

    /// <summary>
    /// Only used for internal vanilla events and specifies the event should always be called last.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    Internal
}
