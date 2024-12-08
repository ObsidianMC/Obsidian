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
            if (value is IBlock)
            {
                if (TagsRegistry.Block.GravityAffected.Entries.Contains(value.RegistryId))
                {
                    Delay = 1;
                }
                else if (value.Material == Material.Lava)
                {
                    Delay = 40;
                }
                else if (value.Material == Material.Water)
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
