using Obsidian.API.Inventory;
using Obsidian.Events;
using Obsidian.Nbt;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// The items a chest boat or a minecart with a chest or hopper carries, like vanilla's <c>ContainerEntity</c>: they
/// may wait for a loot table, rolled when a player first opens them or they spill, and they spill where the vehicle
/// breaks.
/// </summary>
internal sealed class VehicleInventory
{
    // Vanilla's ContainerEntity.isChestVehicleStillValid: players further than this from the vehicle lose its screen.
    private const double ReachSquared = 8 * 8;

    public VehicleInventory(int size, string title)
    {
        this.Items = new Container(size) { Title = title, Id = title.ToLowerInvariant().Replace(' ', '_') };
    }

    /// <summary>The slots, which players' screens show.</summary>
    public Container Items { get; }

    /// <summary>
    /// The loot table that fills the slots when they're first opened or spilled, or null once it has.
    /// </summary>
    public string? LootTable { get; set; }

    /// <summary>The seed of <see cref="LootTable"/>; 0 means a random one.</summary>
    public long LootTableSeed { get; set; }

    /// <summary>Taken while the loot table is read, rolled or saved.</summary>
    public object LootLock { get; } = new();

    // 1 when item access changed the slots since the vehicle last told the players looking at them.
    private int changed;

    /// <summary>
    /// The way into the slots for hoppers and droppers (vanilla's <c>ContainerEntity</c> as a <c>Container</c>):
    /// every slot, from any side.
    /// </summary>
    public IItemAccess Access(VehicleEntity vehicle) => new VehicleItemAccess(this, vehicle);

    /// <summary>Notes that item access changed the slots, which the vehicle's next tick shows its viewers.</summary>
    internal void MarkChanged() => Interlocked.Exchange(ref this.changed, 1);

    /// <summary>
    /// Vanilla's <c>readChestVehicleSaveData</c>: the loot table, or the items when there's none.
    /// </summary>
    public void Load(NbtCompound tag)
    {
        using var held = this.Items.Lock();
        lock (this.LootLock)
        {
            for (var slot = 0; slot < this.Items.Size; slot++)
                this.Items.SetItem(slot, null);

            this.LootTable = tag.TryGetTagValue<string>("LootTable", out var lootTable) ? lootTable : null;
            this.LootTableSeed = tag.TryGetNumber<long>("LootTableSeed", out var seed) ? seed : 0;

            if (this.LootTable is not null || !tag.TryGetTag<NbtList>("Items", out var items))
                return;

            foreach (var itemTag in items.OfType<NbtCompound>())
            {
                if (itemTag.TryGetNumber<byte>("Slot", out var slot) && slot < this.Items.Size)
                    this.Items.SetItem(slot, itemTag.ItemFromNbt());
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>addChestVehicleSaveData</c>: the loot table and its seed while it waits, otherwise the items.
    /// </summary>
    public void Save(NbtCompound tag)
    {
        foreach (var name in new[] { "Items", "LootTable", "LootTableSeed" })
            tag.Remove(name);

        using var held = this.Items.Lock();
        lock (this.LootLock)
        {
            if (this.LootTable is not null)
            {
                tag.Add(new NbtTag<string>("LootTable", this.LootTable));
                if (this.LootTableSeed != 0)
                    tag.Add(new NbtTag<long>("LootTableSeed", this.LootTableSeed));

                return;
            }

            var items = new NbtList(NbtTagType.Compound, "Items");
            for (var slot = 0; slot < this.Items.Size; slot++)
            {
                if (this.Items[slot] is not { IsAir: false, Count: > 0 } item)
                    continue;

                var itemTag = item.ToNbt();
                itemTag.Add(new NbtTag<byte>("Slot", (byte)slot));
                items.Add(itemTag);
            }

            tag.Add(items);
        }
    }

    /// <summary>
    /// Opens the slots for <paramref name="player"/>, rolling the loot table first, like vanilla's
    /// <c>interactWithContainerVehicle</c>. Spectators can't open one that still waits for its loot.
    /// </summary>
    public async ValueTask OpenAsync(IPlayer player, VehicleEntity vehicle)
    {
        if (vehicle.Level is not AbstractLevel level)
            return;

        if (this.LootTable is not null && player.GameMode == GameMode.Spectator)
            return;

        level.UnpackVehicleLoot(this, player, vehicle.Position);
        this.Items.Title = vehicle.CustomName ?? this.Items.Title;
        await player.OpenInventoryAsync(this.Items);
    }

    /// <summary>
    /// Vanilla's <c>Containers.dropContents</c> for the vehicle, after its loot table is rolled: every stack spills
    /// around its block.
    /// </summary>
    public async ValueTask DropAsync(VehicleEntity vehicle)
    {
        if (vehicle.Level is not AbstractLevel level)
            return;

        await AbstractLevel.CloseWindowsAsync(this.Items);
        level.UnpackVehicleLoot(this, null, vehicle.Position);

        var contents = new List<ItemStack>();
        using (await this.Items.LockAsync())
        {
            for (var slot = 0; slot < this.Items.Size; slot++)
            {
                if (this.Items[slot] is not { IsAir: false, Count: > 0 } item)
                    continue;

                this.Items.SetItem(slot, null);
                contents.Add(item);
            }
        }

        var block = (Vector)vehicle.Position.Floor();
        foreach (var item in contents)
            level.DropItemStack(block, item);
    }

    /// <summary>
    /// The inventory's part of its vehicle's tick: players looking at it see what hoppers moved, and those that went
    /// too far lose its screen, like vanilla's <c>stillValid</c>.
    /// </summary>
    public async ValueTask TickAsync(VehicleEntity vehicle)
    {
        if (Interlocked.Exchange(ref this.changed, 0) == 1)
            await MainEventHandler.RefreshViewersAsync(this.Items);

        await this.CloseDistantAsync(vehicle);
    }

    private async ValueTask CloseDistantAsync(VehicleEntity vehicle)
    {
        foreach (var viewer in this.Items.GetViewers())
        {
            if (viewer.Level != vehicle.Level
                || (viewer.Position - vehicle.Position).MagnitudeSquared() > ReachSquared)
            {
                if (ReferenceEquals(viewer.OpenedContainer, this.Items))
                    await viewer.CloseContainerAsync(null);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>AbstractContainerMenu.getRedstoneSignalFromContainer</c>: how full the slots are, 0 to 15.
    /// </summary>
    public int RedstoneSignal()
    {
        var fullness = 0f;
        var any = false;
        for (var slot = 0; slot < this.Items.Size; slot++)
        {
            if (this.Items[slot] is not { IsAir: false, Count: > 0 } item)
                continue;

            fullness += item.Count / (float)item.MaxStackSize;
            any = true;
        }

        fullness /= this.Items.Size;
        return (int)Math.Floor(fullness * 14) + (any ? 1 : 0);
    }
}

/// <summary>
/// Item access to a vehicle's slots, all of them from every side, like vanilla's container entities. A vehicle that's
/// gone, or whose slots a player's click holds at the moment, takes and gives nothing; one still waiting for its loot
/// rolls it first, where that's safe.
/// </summary>
internal sealed class VehicleItemAccess(VehicleInventory inventory, VehicleEntity vehicle) : IItemAccess
{
    public int SlotCount => inventory.Items.Size;

    public ItemStack? GetItem(int slot)
    {
        using var gate = this.Enter();
        if (gate is null)
            return null;

        return inventory.Items[this.Index(slot)] is { IsAir: false, Count: > 0 } stack
            ? new ItemStack(stack, stack.Count)
            : null;
    }

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var index = this.Index(slot);
        if (stack.IsAir || stack.Count <= 0)
            return null;

        using var gate = this.Enter();
        if (gate is null)
            return stack;

        // Like vanilla's HopperBlockEntity.tryMoveInItem.
        var existing = inventory.Items[index];
        var empty = existing is null || existing.IsAir || existing.Count <= 0;
        int moved;
        if (empty)
            moved = Math.Min(stack.Count, stack.MaxStackSize);
        else if (existing!.Equals(stack) && existing.Count <= existing.MaxStackSize)
            moved = Math.Clamp(stack.MaxStackSize - existing.Count, 0, stack.Count);
        else
            moved = 0;

        if (moved <= 0)
            return stack;

        if (!simulate)
        {
            inventory.Items.SetItem(index, new ItemStack(stack, empty ? moved : existing!.Count + moved));
            gate.Changed = true;
        }

        return moved == stack.Count ? null : new ItemStack(stack, stack.Count - moved);
    }

    public ItemStack? Extract(int slot, int count, bool simulate = false)
    {
        var index = this.Index(slot);
        if (count <= 0)
            return null;

        using var gate = this.Enter();
        if (gate is null || inventory.Items[index] is not { IsAir: false, Count: > 0 } existing)
            return null;

        var taken = Math.Min(count, existing.Count);
        if (!simulate)
        {
            var rest = existing.Count - taken;
            inventory.Items.SetItem(index, rest > 0 ? new ItemStack(existing, rest) : null);
            gate.Changed = true;
        }

        return new ItemStack(existing, taken);
    }

    private int Index(int slot) => (uint)slot < (uint)inventory.Items.Size
        ? slot
        : throw new ArgumentOutOfRangeException(nameof(slot), slot, null);

    // The slots' lock without waiting, once the loot is rolled, or null when the vehicle can't be reached now.
    private Gate? Enter()
    {
        if (vehicle.IsRemoved || vehicle.Level is not AbstractLevel level)
            return null;

        if (inventory.LootTable is not null)
        {
            // Rolling loot may load chunks, which mustn't happen under the tick or item transfer lock.
            if (level.Fluids.TickLock.IsHeldByCurrentThread || Monitor.IsEntered(ItemEntity.TransferLock))
                return null;

            level.UnpackVehicleLoot(inventory, null, vehicle.Position);
        }

        var transaction = inventory.Items.TransactionLock;
        return transaction.Wait(0) ? new Gate(transaction, inventory) : null;
    }

    private sealed class Gate(SemaphoreSlim held, VehicleInventory inventory) : IDisposable
    {
        public bool Changed { get; set; }

        public void Dispose()
        {
            held.Release();
            if (this.Changed)
                inventory.MarkChanged();
        }
    }
}

/// <summary>
/// A boat or minecart that carries items (vanilla's <c>ContainerEntity</c>), which hoppers, droppers and comparators
/// reach like a container block.
/// </summary>
internal interface IVehicleContainer
{
    /// <summary>The way into its slots.</summary>
    IItemAccess ItemAccess { get; }

    /// <summary>How full its slots are, 0 to 15.</summary>
    int RedstoneSignal { get; }
}
