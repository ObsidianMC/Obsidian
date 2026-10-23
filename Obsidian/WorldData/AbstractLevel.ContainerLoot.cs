using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.WorldData.Maps;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Fills pending container loot once, preparing it without world or inventory locks because explorer maps load
    /// chunks. Call without the tick, item transfer, click or loot locks; removal may fill an already removed entity.
    /// </summary>
    internal void UnpackLootTable(ContainerBlockEntity container, IEntity? opener, bool removing = false)
    {
        if (this.Fluids.TickLock.IsHeldByCurrentThread
            || System.Threading.Monitor.IsEntered(ItemEntity.TransferLock))
        {
            throw new InvalidOperationException("Container loot must be prepared outside the tick and transfer locks.");
        }

        lock (container.LootGenerationLock)
        {
            var inventory = container.Inventory;
            while (true)
            {
                string lootTable;
                long seed;
                long revision;
                bool retired;
                var staged = new LootInventory(inventory.Size);
                using (inventory.Lock())
                lock (container.LootLock)
                {
                    if (container.LootTable is not string pending || container.IsRemoved && !removing)
                        return;

                    lootTable = pending;
                    seed = container.LootTableSeed;
                    revision = inventory.Revision;
                    retired = inventory.IsRetired;
                    for (var slot = 0; slot < inventory.Size; slot++)
                    {
                        if (inventory[slot] is ItemStack item)
                            staged.SetItem(slot, new ItemStack(item, item.Count));
                    }
                }

                // Saves still see the pending table while preparation runs. Match vanilla's random draws across
                // generation, slot shuffling and splitting, including when block and container tables share a source.
                var resolver = PluginLootTables.CaptureResolver();
                var table = resolver(lootTable) ?? LootTable.Empty;
                var random = table.CreateRandom(seed, BlockLoot.SequencesFor(this));
                var position = container.BlockPosition;
                lock (random)
                {
                    table.Fill(staged, new LootContext
                    {
                        Random = random,
                        Origin = new VectorD(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
                        ThisEntity = opener,
                        ResolveTable = resolver,
                        ExplorationMaps = ExplorationMaps.For(this)
                    });
                }

                using (inventory.Lock())
                lock (container.LootLock)
                {
                    // Eviction/unload saved the pending table, or another break now owns retirement. Its snapshot
                    // must remain final; that operation will prepare loot itself when it needs to.
                    if (container.IsRemoved && !removing || inventory.IsRetired != retired)
                        return;

                    if (inventory.Revision != revision
                        || container.LootTable != lootTable
                        || container.LootTableSeed != seed)
                        continue;

                    for (var slot = 0; slot < inventory.Size; slot++)
                        inventory.SetItem(slot, staged[slot]);

                    container.LootTable = null;
                    container.LootTableSeed = 0;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Fills a chest boat's or minecart's slots from the loot table they wait for, if any, like vanilla's
    /// <c>unpackChestVehicleLootTable</c>: the table rolls from the vehicle's position, with <paramref name="opener"/>
    /// as the entity it's for.
    /// </summary>
    internal void UnpackVehicleLoot(Entities.VehicleInventory vehicle, IEntity? opener, VectorD origin)
    {
        string lootTable;
        long seed;
        var inventory = vehicle.Items;
        var staged = new LootInventory(inventory.Size);
        using (inventory.Lock())
        lock (vehicle.LootLock)
        {
            if (vehicle.LootTable is not string pending)
                return;

            lootTable = pending;
            seed = vehicle.LootTableSeed;
            for (var slot = 0; slot < inventory.Size; slot++)
            {
                if (inventory[slot] is ItemStack item)
                    staged.SetItem(slot, new ItemStack(item, item.Count));
            }
        }

        var resolver = PluginLootTables.CaptureResolver();
        var table = resolver(lootTable) ?? LootTable.Empty;
        var random = table.CreateRandom(seed, BlockLoot.SequencesFor(this));
        lock (random)
        {
            table.Fill(staged, new LootContext
            {
                Random = random,
                Origin = origin,
                ThisEntity = opener,
                ResolveTable = resolver,
                ExplorationMaps = ExplorationMaps.For(this)
            });
        }

        using (inventory.Lock())
        lock (vehicle.LootLock)
        {
            // Another opener rolled it meanwhile.
            if (vehicle.LootTable != lootTable)
                return;

            for (var slot = 0; slot < inventory.Size; slot++)
                inventory.SetItem(slot, staged[slot]);

            vehicle.LootTable = null;
            vehicle.LootTableSeed = 0;
        }
    }

    // A host-owned buffer accepts every container size without inheriting its plugin's slot behavior.
    private sealed class LootInventory(int size) : BaseContainer(size);
}
