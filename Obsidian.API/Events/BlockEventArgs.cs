namespace Obsidian.API.Events;

/// <summary>
/// The base class for events about a single block in a level.
/// </summary>
public abstract class BlockEventArgs(IServer server, IBlock block, Vector location, ILevel level) : BaseMinecraftEventArgs(server)
{
    /// <summary>
    /// The impacted block.
    /// </summary>
    public IBlock Block { get; } = block;

    /// <summary>
    /// Location of the impacted block.
    /// </summary>
    public Vector Location { get; } = location;

    /// <summary>
    /// Level where the event took place.
    /// </summary>
    public ILevel Level { get; } = level;


    /// <summary>
    /// Gets the client's block-change sequence number for the action that raised the event, which the server
    /// acknowledges once it has handled the change.
    /// </summary>
    public int Sequence { get; init; }
}
