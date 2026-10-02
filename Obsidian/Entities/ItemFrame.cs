using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item_frame")]
public partial class ItemFrame : Entity
{
    public ItemStack? Item { get; set; }

    /// <summary>The item's rotation in the frame, in eighths of a turn.</summary>
    public int Rotation { get; set; }

    /// <summary>The direction the frame faces, away from the block it hangs on.</summary>
    public BlockFace Facing { get; set; } = BlockFace.South;

    /// <summary>
    /// Like vanilla, the spawn packet carries the facing (as a 3D direction id), which the client needs to hang the frame.
    /// </summary>
    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) => base.SpawnEntity(velocity, FacingId(this.Facing));

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Direction);
        writer.WriteVarInt(FacingId(this.Facing));

        writer.WriteEntityMetadataType(9, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);

        writer.WriteEntityMetadataType(10, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.Rotation);
    }

    /// <summary>Vanilla's 3D direction ids: down, up, north, south, west, east.</summary>
    internal static int FacingId(BlockFace face) => face switch
    {
        BlockFace.Down => 0,
        BlockFace.Up => 1,
        BlockFace.North => 2,
        BlockFace.South => 3,
        BlockFace.West => 4,
        _ => 5
    };

    internal static BlockFace FromFacingId(int id) => id switch
    {
        0 => BlockFace.Down,
        1 => BlockFace.Up,
        2 => BlockFace.North,
        3 => BlockFace.South,
        4 => BlockFace.West,
        _ => BlockFace.East
    };
}
