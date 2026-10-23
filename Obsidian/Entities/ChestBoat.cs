using Obsidian.API.Inventory;
using Obsidian.Nbt;

namespace Obsidian.Entities;

/// <summary>
/// A boat or bamboo raft with a chest, like vanilla's <c>AbstractChestBoat</c>: it seats one, in front of the chest,
/// and its 27 slots open for a player who sneaks as they use it, or uses it while it's taken.
/// </summary>
[MinecraftEntity("minecraft:oak_chest_boat")]
public sealed partial class ChestBoat : Boat, IVehicleContainer
{
    private readonly VehicleInventory inventory = new(27, "Chest Boat");

    public ChestBoat() => this.Type = EntityType.OakChestBoat;

    /// <summary>The chest's slots.</summary>
    public BaseContainer Inventory => this.inventory.Items;

    /// <summary>The loot table the chest waits for, if any.</summary>
    public string? LootTable
    {
        get => this.inventory.LootTable;
        set => this.inventory.LootTable = value;
    }

    /// <summary>The seed of <see cref="LootTable"/>; 0 means a random one.</summary>
    public long LootTableSeed
    {
        get => this.inventory.LootTableSeed;
        set => this.inventory.LootTableSeed = value;
    }

    IItemAccess IVehicleContainer.ItemAccess => this.inventory.Access(this);

    int IVehicleContainer.RedstoneSignal => this.inventory.RedstoneSignal();

    protected override int MaximumPassengers => 1;

    private protected override float SinglePassengerOffset => 0.15f;

    // Vanilla's AbstractChestBoat.interact: the chest opens when the boat can't take the player or they sneak.
    public override async ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand)
    {
        if (await base.InteractAsync(player, hand))
            return true;

        if (this.CanAddPassenger(player) && !player.Sneaking)
            return false;

        await this.inventory.OpenAsync(player, this);
        return true;
    }

    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        if (!this.IsRemoved)
            await this.inventory.TickAsync(this);
    }

    private protected override ValueTask OnDiscardedAsync() => this.inventory.DropAsync(this);

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        this.inventory.Save(tag);
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.inventory.Load(tag);
    }
}
