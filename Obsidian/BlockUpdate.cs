using Obsidian.WorldData;

namespace Obsidian;

public struct BlockUpdate
{
    internal readonly World world;
    internal Vector position;

    internal int Delay { get; private set; }
    internal int delayCounter;

    internal Block? Block
    {
        get => _block;
        set {
            _block = value;
            if (value is Block b)
            {
                if (API.Block.GravityAffected.Contains(b.Material))
                {
                    Delay = 1;
                }
                else if (b.Material == Material.Lava)
                {
                    Delay = 40;
                }
                else if (b.Material == Material.Water)
                {
                    Delay = 5;
                }
            }
            delayCounter = Delay;
        }
    }
    private Block? _block;

    public BlockUpdate(World w, Vector pos, Block? blk = null)
    {
        world = w;
        position = pos;
        Delay = 0;
        delayCounter = Delay;
        _block = null;
        Block = blk;
    }
}
