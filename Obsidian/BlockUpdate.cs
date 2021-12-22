using Obsidian.WorldData;

namespace Obsidian;

public struct BlockUpdate
{
    internal readonly World world;
    internal Vector position;
    internal int delay { get; private set; }
    internal int delayCounter;
    private Block? _block;
    internal Block? block
    {
        get => _block;
        set
        {
            _block = value;
            if (value is Block b)
            {
                if (Block.GravityAffected.Contains(b.Material))
                {
                    delay = 1;
                }
                else if (b.Material == Material.Lava)
                {
                    delay = 40;
                }
                else if (b.Material == Material.Water)
                {
                    delay = 5;
                }
            }
            delayCounter = delay;
        }
    }

    public BlockUpdate(World w, Vector pos, Block? blk = null)
    {
        world = w;
        position = pos;
        delay = 0;
        delayCounter = delay;
        _block = null;
        block = blk;
    }
}
