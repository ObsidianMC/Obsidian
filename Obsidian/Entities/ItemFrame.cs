using Obsidian.API.Inventory;
using Obsidian.Nbt;

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

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.SetOrRemove("Item", this.Item is { IsAir: false } item ? item.ToNbt("Item") : null);
        tag.Set(new NbtTag<byte>("ItemRotation", (byte)this.Rotation));
        tag.Set(new NbtTag<byte>("Facing", (byte)FacingId(this.Facing)));

        // The block the frame is in; its position is offset from the block's center towards the block it hangs on.
        var position = this.Position;
        tag.Set(new NbtArray<int>("block_pos", [(int)MathF.Floor(position.X), (int)MathF.Floor(position.Y), (int)MathF.Floor(position.Z)]));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.Item = tag.TryGetTag<NbtCompound>("Item", out var item) ? item.ItemFromNbt() : null;
        this.Rotation = tag.TryGetTag<NbtTag<byte>>("ItemRotation", out var rotation) ? rotation.Value : 0;

        // Vanilla's default facing is down.
        this.Facing = FromFacingId(tag.TryGetTag<NbtTag<byte>>("Facing", out var facing) ? facing.Value : 0);
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
