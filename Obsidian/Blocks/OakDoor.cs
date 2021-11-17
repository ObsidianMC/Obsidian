using Obsidian.Utilities.Registry.Enums;

namespace Obsidian.Blocks;

public struct OakDoor
{
    public EHalf Half { get => (EHalf)((state >> halfShift) & halfBits); set => state = (state & halfFilter) | ((int)value << halfShift); }
    public Direction Face { get => (Direction)((state >> faceShift) & faceBits); set => state = (state & faceFilter) | ((int)value << faceShift); }
    public bool Open { get => ((state >> openShift) & openBits) == 1; set => state = (state & openFilter) | ((value ? 1 : 0) << openShift); }
    public Hinge Hinge { get => (Hinge)((state >> hingeShift) & hingeBits); set => state = (state & hingeFilter) | ((int)value << hingeShift); }
    public bool Powered { get => ((state >> poweredShift) & poweredBits) == 1; set => state = (state & poweredFilter) | ((value ? 1 : 0) << poweredShift); }

    public static readonly string UnlocalizedName = "Oak Door";
    public static int Id => 161;
    public int StateId => baseId + state;
    public int State => state;
    public static int BaseId => baseId;

    private int state;

    #region Constants
    private const int baseId = 3573;

    private const int poweredFilter = 0b_1111_1110;
    private const int openFilter = 0b_1111_1101;
    private const int hingeFilter = 0b_1111_1011;
    private const int halfFilter = 0b_1111_0111;
    private const int faceFilter = 0b_0000_1111;

    private const int poweredShift = 0;
    private const int openShift = 1;
    private const int hingeShift = 2;
    private const int halfShift = 3;
    private const int faceShift = 4;

    private const int poweredBits = 1;
    private const int openBits = 1;
    private const int hingeBits = 1;
    private const int halfBits = 1;
    private const int faceBits = 4;
    #endregion

    public OakDoor(int state)
    {
        this.state = state;
    }

    public OakDoor(EHalf half, Direction face, bool open, Hinge hinge, bool powered)
    {
        state = powered ? 1 : 0;
        state |= open ? 2 : 0;
        state |= (int)hinge << hingeShift;
        state |= (int)half << halfShift;
        state |= (int)face << faceShift;
    }

    public static implicit operator Block(OakDoor oakDoor)
    {
        return new Block(BaseId, oakDoor.state);
    }

    public static explicit operator OakDoor(Block block)
    {
        if (block.BaseId == baseId)
            return new OakDoor(block.StateId - baseId);
        throw new InvalidCastException($"Cannot cast {block.Name} to {UnlocalizedName}");
    }
}
