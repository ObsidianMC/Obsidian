using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    internal Func<RecipeSet>? RecipeSource { get; set; }

    internal RecipeSet CookingRecipes => this is Dimension { ParentWorld: AbstractLevel parent }
        ? parent.CookingRecipes
        : this.RecipeSource?.Invoke() ?? RecipesRegistry.Current;

    /// <summary>Ticks a resident furnace; loot and packet delivery run outside its locks.</summary>
    internal async ValueTask TickFurnaceAsync(FurnaceBlockEntity machine)
    {
        if (machine.LootTable is not null)
            this.UnpackLootTable(machine, null);

        var inventory = machine.Inventory;
        FurnaceBlockEntity.Step step;
        bool properties;
        lock (this.Fluids.TickLock)
        {
            if (machine.IsRemoved || !ReferenceEquals(this.GetResidentBlockEntity(machine.BlockPosition), machine))
                return;

            using (inventory.Lock())
            {
                if (inventory.ShowsRetired)
                    return;

                var before = (machine.LitRemaining, machine.LitTotal, machine.CookingSpent, machine.CookingTotal);
                step = machine.Cook(this.CookingRecipes);
                properties = before !=
                    (machine.LitRemaining, machine.LitTotal, machine.CookingSpent, machine.CookingTotal);
            }

            // Neighbour hooks may reach this inventory; publish the lit state after releasing its lock.
            var block = this.BlockUpdates.GetBlock(machine.BlockPosition);
            var lit = machine.LitRemaining > 0;
            if (block.GetProperty("lit") != (lit ? "true" : "false"))
                this.BlockUpdates.SetBlock(machine.BlockPosition, block.WithProperty("lit", lit));
        }

        if (step.Remainder is ItemStack remainder)
            this.DropItemStack(machine.BlockPosition, remainder);

        if (step.Slots)
            await MainEventHandler.RefreshViewersAsync(inventory);

        if (properties)
        {
            foreach (var viewer in inventory.GetViewers())
            {
                if (ReferenceEquals(viewer.OpenedContainer, inventory))
                    await SendFurnacePropertiesAsync(viewer, machine.Furnace);
            }
        }
    }

    /// <summary>Captures the furnace menu's four data slots together and sends them outside its lock.</summary>
    internal static async ValueTask SendFurnacePropertiesAsync(IPlayer viewer, SmeltingContainer furnace)
    {
        if (FurnaceBlockEntity.Of(furnace) is not FurnaceBlockEntity machine)
            return;

        short[] values;
        using (furnace.Lock())
        {
            values = [(short)machine.LitRemaining, (short)machine.LitTotal,
                (short)machine.CookingSpent, (short)machine.CookingTotal];
        }

        var containerId = viewer.CurrentContainerId;
        for (var index = 0; index < values.Length; index++)
        {
            if (!ReferenceEquals(viewer.OpenedContainer, furnace) || viewer.CurrentContainerId != containerId)
                return;

            await viewer.QueuePacketAsync(new ContainerSetDataPacket
            {
                ContainerId = containerId,
                WindowProperty = new FurnaceWindowProperty((FurnaceProperty)index, values[index])
            });
        }
    }

    internal void DropCookingExperience(VectorD position, int amount)
    {
        while (amount > 0)
        {
            var batch = (short)Math.Min(short.MaxValue, amount);
            this.SpawnExperienceOrbs(position, batch);
            amount -= batch;
        }
    }

    /// <summary>Places one held cooking ingredient in the first free campfire slot.</summary>
    internal async ValueTask<bool> UseCampfireAsync(IClientPlayer player, Vector position, int handSlot)
    {
        ItemStack? held;
        CampfireBlockEntity campfire;
        lock (this.Fluids.TickLock)
        {
            if (player.Sneaking || player.GameMode == GameMode.Spectator || player.Health <= 0
                || (player.Position - (VectorD)position).MagnitudeSquared() > 36
                || this.GetResidentBlockEntity(position) is not CampfireBlockEntity resident)
            {
                return false;
            }

            campfire = resident;
            using (player.Inventory.Lock())
            using (campfire.Inventory.Lock())
            {
                if (campfire.IsRemoved || campfire.Inventory.ShowsRetired)
                    return false;

                var stack = player.Inventory[handSlot];
                var recipe = stack.IsNullOrAir()
                    ? null
                    : this.CookingRecipes.FindCooking(CraftingType.CampfireCooking, stack!);
                if (recipe is null || !campfire.Place(stack!, recipe.CookingTime))
                    return false;

                if (player.GameMode != GameMode.Creative)
                    player.Inventory.RemoveItem(handSlot, 1);

                held = player.Inventory[handSlot] is ItemStack current
                    ? Net.NetworkBuffer.SnapshotItemStack(current)
                    : null;
            }
        }

        await player.QueuePacketAsync(new ContainerSetSlotPacket
        {
            ContainerId = 0,
            Slot = (short)handSlot,
            SlotData = held
        });
        campfire.SendUpdate();
        return true;
    }

    internal ValueTask TickCampfireAsync(CampfireBlockEntity campfire)
    {
        IReadOnlyList<ItemStack> finished;
        lock (this.Fluids.TickLock)
        {
            if (campfire.IsRemoved || !ReferenceEquals(this.GetResidentBlockEntity(campfire.BlockPosition), campfire))
                return ValueTask.CompletedTask;

            var block = this.BlockUpdates.GetBlock(campfire.BlockPosition);
            using (campfire.Inventory.Lock())
                finished = campfire.Cook(this.CookingRecipes, block.GetProperty("lit") == "true");
        }

        foreach (var stack in finished)
            this.DropItemStack(campfire.BlockPosition, stack);

        if (finished.Count > 0)
            campfire.SendUpdate();

        return ValueTask.CompletedTask;
    }
}
