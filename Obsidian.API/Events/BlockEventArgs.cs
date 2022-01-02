namespace Obsidian.API.Events;

public abstract class BlockEventArgs : BaseMinecraftEventArgs
{
    /// <summary>
    /// The impacted block.
    /// </summary>
    public Block Block { get; }

    /// <summary>
    /// Location of the impacted block.
    /// </summary>
    public Vector Location { get; }

    /// <summary>
    /// World where the event took place.
    /// </summary>
    public IWorld World { get; }

    protected BlockEventArgs(IServer server, Block block, Vector location) : base(server)
    {
        Block = block;
        Location = location;
        World = server.DefaultWorld;
    }
}
