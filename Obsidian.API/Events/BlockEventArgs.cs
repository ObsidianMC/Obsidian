namespace Obsidian.API.Events;

public abstract class BlockEventArgs(IServer server, IBlock block, Vector location) : BaseMinecraftEventArgs(server)
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
    /// World where the event took place.
    /// </summary>
    public IWorld World { get; } = server.DefaultWorld;


    public int Sequence { get; init; }
}
