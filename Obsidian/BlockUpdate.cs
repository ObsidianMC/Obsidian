using Obsidian.Utilities.Registry;
using Obsidian.WorldData;

namespace Obsidian;

public struct BlockUpdate
{
    internal readonly World world;
    internal Vector position;

    internal int Delay { get; private set; }
    internal int delayCounter;

    internal IBlock? Block
    {
        get => _block;
        set
        {
            _block = value;
            if (value is IBlock b)
            {
                if (TagsRegistry.Blocks.GravityAffected.Entries.Contains(b.State.Id))
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
    private IBlock? _block;

    public BlockUpdate(World w, Vector pos, IBlock? blk = null)
    {
        world = w;
        position = pos;
        Delay = 0;
        delayCounter = Delay;
        _block = null;
        Block = blk;
    }
}
