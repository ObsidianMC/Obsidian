namespace Obsidian.API.Events;

public sealed class BlockBreakEventArgs : BaseEventArgs, IBlockEvent, IPlayerEvent, ICancellable
{
    internal BlockBreakEventArgs(IWorld world, IPlayer player, Block block, Vector location)
    {
        World = world;
        Player = player;
        Block = block;
        Location = location;
    }

    public IWorld World { get; }
    public IPlayer Player { get; }
    public Block Block { get; }
    public Vector Location { get; }
    public bool Cancelled { get; set; }
}
