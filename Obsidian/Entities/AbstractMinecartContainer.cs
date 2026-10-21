using Obsidian.API.Inventory;
using Obsidian.Nbt;

namespace Obsidian.Entities;

/// <summary>
/// A minecart that carries items, like vanilla's <c>AbstractMinecartContainer</c>: using it opens its slots, it
/// spills them when it breaks, and it rolls a little further the emptier it is.
/// </summary>
public abstract class AbstractMinecartContainer : AbstractMinecart, IVehicleContainer
{
    private protected AbstractMinecartContainer(int size, string title)
    {
        this.Contents = new VehicleInventory(size, title);
    }

    /// <summary>The slots, their loot table and its seed.</summary>
    internal VehicleInventory Contents { get; }

    /// <summary>The slots.</summary>
    public BaseContainer Inventory => this.Contents.Items;

    /// <summary>The loot table the slots wait for, if any.</summary>
    public string? LootTable
    {
        get => this.Contents.LootTable;
        set => this.Contents.LootTable = value;
    }

    /// <summary>The seed of <see cref="LootTable"/>; 0 means a random one.</summary>
    public long LootTableSeed
    {
        get => this.Contents.LootTableSeed;
        set => this.Contents.LootTableSeed = value;
    }

    IItemAccess IVehicleContainer.ItemAccess => this.Contents.Access(this);

    int IVehicleContainer.RedstoneSignal => this.Contents.RedstoneSignal();

    // Vanilla's interactWithContainerVehicle: using it opens it, sneaking or not.
    public override async ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand)
    {
        await this.Contents.OpenAsync(player, this);
        return true;
    }

    // Vanilla's AbstractMinecartContainer.applyNaturalSlowdown: 0.98, plus a thousandth for each step of the
    // comparator signal it would give short of full (unless its loot is still to come), less in water.
    private protected override VectorD ApplyNaturalSlowdown(VectorD motion)
    {
        var factor = 0.98f;
        if (this.LootTable is null)
            factor += (15 - this.Contents.RedstoneSignal()) * 0.001f;

        if (this.InWater)
            factor *= 0.95f;

        return new VectorD(motion.X * factor, 0, motion.Z * factor);
    }

    private protected override async ValueTask AfterMoveAsync() => await this.Contents.TickAsync(this);

    private protected override ValueTask OnDiscardedAsync() => this.Contents.DropAsync(this);

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        this.Contents.Save(tag);
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.Contents.Load(tag);
    }
}
