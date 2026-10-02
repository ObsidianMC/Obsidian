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
            // Only falling blocks wait; fluids run through their own scheduled ticks (see LevelFluids).
            if (value is not null && value.IsGravityAffected())
            {
                Delay = 1;
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
