using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.WorldData.Maps;

namespace Obsidian.Entities;

/// <summary>
/// An item frame, like vanilla's <c>ItemFrame</c>: using it puts the held item in, or turns the item it holds an
/// eighth of a turn; hitting it takes the item out, or breaks the empty frame.
/// </summary>
[MinecraftEntity("minecraft:item_frame")]
public partial class ItemFrame : HangingEntity
{
    public ItemFrame() => this.Type = EntityType.ItemFrame;

    public ItemStack? Item { get; set; }

    /// <summary>The item's rotation in the frame, in eighths of a turn.</summary>
    public int Rotation { get; set; }

    /// <summary>Whether players can't take its item or break it, like vanilla's <c>Fixed</c> frames.</summary>
    public bool Fixed { get; set; }

    protected override Item DropItem => ItemsRegistry.ItemFrame;

    protected override string BreakSound => "minecraft:entity.item_frame.break";

    protected override string PlaceSound => "minecraft:entity.item_frame.place";

    protected virtual string AddItemSound => "minecraft:entity.item_frame.add_item";

    protected virtual string RemoveItemSound => "minecraft:entity.item_frame.remove_item";

    protected virtual string RotateItemSound => "minecraft:entity.item_frame.rotate_item";

    /// <summary>
    /// The frame's box, worked out from where it hangs whenever it's read, so that it holds for frames loaded,
    /// generated or moved alike.
    /// </summary>
    /// <remarks>
    /// Vanilla's hanging box, which comparators find frames by: centred 0.46875 from the middle of its block toward
    /// the block it hangs on, a sixteenth of a block deep, and three quarters of a block across, or a whole block
    /// holding a filled map. Its block is the one its position is in, as saved in <c>block_pos</c>.
    /// </remarks>
    public override BoundingBox BoundingBox
    {
        get
        {
            const double offset = 0.46875;
            const double depth = 0.0625;

            var position = this.Position;
            var step = (VectorD)this.Facing.ToVector();
            var center = new VectorD(
                Math.Floor(position.X) + 0.5 - step.X * offset,
                Math.Floor(position.Y) + 0.5 - step.Y * offset,
                Math.Floor(position.Z) + 0.5 - step.Z * offset);

            var across = this.Item is ItemStack item && MapStorage.GetMapId(item) is not null ? 1.0 : 0.75;
            var half = new VectorD(
                (step.X != 0 ? depth : across) / 2,
                (step.Y != 0 ? depth : across) / 2,
                (step.Z != 0 ? depth : across) / 2);

            return new BoundingBox(center - half, center + half);
        }
    }

    public override async ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand)
    {
        if (this.Fixed || player.GameMode == GameMode.Spectator)
            return false;

        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var held = player.Inventory.GetItem(slot);

        if (this.Item is not { IsAir: false })
        {
            if (held is not { IsAir: false, Count: > 0 } || player.GameMode == GameMode.Adventure)
                return false;

            this.Item = new ItemStack(held, 1);
            this.Rotation = 0;
            this.PlaySound(this.AddItemSound);

            if (player.GameMode != GameMode.Creative)
            {
                player.Inventory.RemoveItem(slot, 1);
                await player.SyncInventorySlotAsync(slot);
            }
        }
        else
        {
            this.Rotation = (this.Rotation + 1) % 8;
            this.PlaySound(this.RotateItemSound);
        }

        this.SynchronizeMetadata();
        this.UpdateComparators();
        return true;
    }

    /// <summary>Hitting a frame takes its item out, or breaks it once it's empty.</summary>
    public override async ValueTask AttackedAsync(PlayerBase player)
    {
        if (this.Fixed)
            return;

        if (this.Item is not { IsAir: false } item)
        {
            await base.AttackedAsync(player);
            return;
        }

        this.Item = null;
        this.Rotation = 0;
        this.PlaySound(this.RemoveItemSound);

        if (player.GameMode != GameMode.Creative && this.Level.LevelData.GetBooleanRule("entity_drops"))
            this.Level.SpawnItem(this.Position, item);

        this.SynchronizeMetadata();
        this.UpdateComparators();
    }

    protected override void DropContents(bool withSelf)
    {
        if (this.Item is { IsAir: false } item)
            this.Level.SpawnItem(this.Position, item);

        this.Item = null;
        base.DropContents(withSelf);
    }

    // Comparators reading the frame see its new item or rotation.
    private void UpdateComparators()
    {
        if (this.Level is WorldData.AbstractLevel level)
            level.UpdateNeighbors(this.BlockPosition - this.Facing.ToVector(), BlocksRegistry.Air);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

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
        tag.SetFlag("Fixed", this.Fixed);
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.Item = tag.TryGetTag<NbtCompound>("Item", out var item) ? item.ItemFromNbt() : null;
        this.Rotation = tag.TryGetTag<NbtTag<byte>>("ItemRotation", out var rotation) ? rotation.Value : 0;
        this.Fixed = tag.TryGetBool("Fixed", out var isFixed) && isFixed;
    }
}

/// <summary>
/// A glow item frame, like vanilla's <c>GlowItemFrame</c>: an item frame whose item glows.
/// </summary>
[MinecraftEntity("minecraft:glow_item_frame")]
public sealed partial class GlowItemFrame : ItemFrame
{
    public GlowItemFrame() => this.Type = EntityType.GlowItemFrame;

    protected override Item DropItem => ItemsRegistry.GlowItemFrame;

    protected override string BreakSound => "minecraft:entity.glow_item_frame.break";

    protected override string PlaceSound => "minecraft:entity.glow_item_frame.place";

    protected override string AddItemSound => "minecraft:entity.glow_item_frame.add_item";

    protected override string RemoveItemSound => "minecraft:entity.glow_item_frame.remove_item";

    protected override string RotateItemSound => "minecraft:entity.glow_item_frame.rotate_item";
}
