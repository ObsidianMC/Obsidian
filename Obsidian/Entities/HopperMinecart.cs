using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with a hopper, like vanilla's <c>MinecartHopper</c>: five slots that each tick take in one item from the
/// container above, or the item entities on and above it. An activator rail turns it off while powered and on again
/// while not.
/// </summary>
[MinecraftEntity("minecraft:hopper_minecart")]
public sealed partial class HopperMinecart : AbstractMinecartContainer
{
    // Vanilla's Hopper.SUCK_AABB above the hopper's level position, which is half a block above the cart.
    private const double SuckBottom = 11 / 16.0;
    private const double SuckTop = 2;

    public HopperMinecart() : base(5, "Minecart with Hopper") => this.Type = EntityType.HopperMinecart;

    /// <summary>Whether it takes in items (vanilla's <c>Enabled</c>).</summary>
    public bool Enabled { get; set; } = true;

    protected override int DefaultDisplayOffset => 1;

    protected override Item DropItem => ItemsRegistry.Get(ItemIds.HopperMinecart);

    // Vanilla's MinecartHopper.activateMinecart: enabled exactly while the activator rail isn't powered.
    private protected override ValueTask ActivateAsync(Vector rail, bool powered)
    {
        this.Enabled = !powered;
        return default;
    }

    private protected override async ValueTask AfterMoveAsync()
    {
        await base.AfterMoveAsync();
        if (this.IsRemoved || !this.Enabled || this.Level is not AbstractLevel level)
            return;

        if (await this.SuckInAsync(level))
            this.Contents.MarkChanged();
    }

    // Vanilla's MinecartHopper.suckInItems: HopperBlockEntity.suckInItems from the cart's level position (half a block
    // up), then the item entities around the cart itself.
    private async ValueTask<bool> SuckInAsync(AbstractLevel level)
    {
        var own = this.Contents.Access(this);
        if (Hoppers.IsFull(own))
            return false;

        var above = (Vector)new VectorD(this.Position.X, this.Position.Y + 1.5, this.Position.Z).Floor();
        if (level.GetItemAccess(above, BlockFace.Down) is IItemAccess source)
        {
            for (var slot = 0; slot < source.SlotCount; slot++)
            {
                var moved = Hoppers.TryMoveOne(source, slot, own, out var leftover);
                Hoppers.DropLeftover(level, above, leftover);
                if (moved)
                    return true;
            }

            return false;
        }

        // Unlike a hopper block's, a cart's isn't blocked by a solid block above it.
        var levelY = this.Position.Y + 0.5;
        var bowl = level.BlockUpdates.EntitiesIn(
            new VectorD(this.Position.X - 0.5, levelY - 0.5 + SuckBottom, this.Position.Z - 0.5),
            new VectorD(this.Position.X + 0.5, levelY - 0.5 + SuckTop, this.Position.Z + 0.5));
        foreach (var item in bowl.OfType<ItemEntity>().OrderBy(item => item.EntityId).ToList())
        {
            if (await Hoppers.TakeInAsync(own, item))
                return true;
        }

        var box = this.Dimension.CreateBBFromPosition(this.Position);
        var around = level.BlockUpdates.EntitiesIn(box.Min - new VectorD(0.25, 0, 0.25), box.Max + new VectorD(0.25, 0, 0.25));
        var took = false;
        foreach (var item in around.OfType<ItemEntity>().OrderBy(item => item.EntityId).ToList())
            took |= await Hoppers.TakeInAsync(own, item);

        return took;
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<bool>("Enabled", this.Enabled));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.Enabled = !tag.TryGetBool("Enabled", out var enabled) || enabled;
    }
}
