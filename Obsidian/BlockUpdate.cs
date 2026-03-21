using Obsidian.API.World;

namespace Obsidian;

public struct BlockUpdate : IBlockUpdate
{
    public ILevel Level { get; }
    public Vector Position { get; set; }

    public int Delay { get; set; }
    public int DelayCounter { get; set; }

    public IBlock? Block
    {
        readonly get => field;
        set
        {
            field = value;
            if (value is not null)
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
            DelayCounter = Delay;
        }
    }

    public BlockUpdate(ILevel level, Vector pos, IBlock? blk = null)
    {
        Level = level;
        Position = pos;
        Delay = 0;
        DelayCounter = Delay;
        Block = blk;
    }
}
