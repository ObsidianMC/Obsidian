namespace Obsidian.API.Events;

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


    public int Sequence { get; init; }
}
