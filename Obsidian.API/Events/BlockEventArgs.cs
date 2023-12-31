namespace Obsidian.API.Events;

public abstract class BlockEventArgs : BaseMinecraftEventArgs
{
    public static new string Name => "BlockEvent";

    /// <summary>
    /// The impacted block.
    /// </summary>
    public IBlock Block { get; }

    /// <summary>
    /// Location of the impacted block.
    /// </summary>
    public Vector Location { get; }

    /// <summary>
    /// World where the event took place.
    /// </summary>
    public IWorld World { get; }

    protected BlockEventArgs(IServer server, IBlock block, Vector location) : base(server)
    {
        Block = block;
        Location = location;
        World = server.DefaultWorld;
    }
}
