using Obsidian.API.Containers;
using Obsidian.API.Data;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Entities;

public partial class Player
{
    // Vanilla's Inventory.dropAll order: hotbar, main inventory, then off hand, feet, legs, chest and head.
    private static readonly int[] deathDropSlots =
    [
        .. Enumerable.Range(36, 9),
        .. Enumerable.Range(9, 27),
        45,
        8,
        7,
        6,
        5
    ];

    private readonly Lock deathState = new();
    private TaskCompletionSource? deathFinished;
    private TaskCompletionSource? respawnFinished;

    // Captured before deferral: callbacks, teleports and respawn cannot change this death's reward or location.
    private sealed record DeathSnapshot(
        ILevel Level,
        VectorD Position,
        double DropY,
        int Experience,
        bool Keeps,
        BaseContainer Window,
        byte WindowId);

    private Task WaitForDeathAsync()
    {
        lock (this.deathState)
            return this.deathFinished?.Task ?? Task.CompletedTask;
    }

    private ValueTask BeginDeathAsync(IEntity source, ChatMessage message)
    {
        DeathSnapshot death;
        TaskCompletionSource finished;
        Task transfers;

        lock (this.deathState)
        {
            if (this.Respawning && this.respawnFinished is TaskCompletionSource respawn)
            {
                return DeferredInventoryWork.RunOrDeferAsync(
                    () => this.KillAfterRespawnAsync(respawn.Task, source, message));
            }

            if (this.progressDead || this.deathFinished is { Task.IsCompleted: false })
                return ValueTask.CompletedTask;

            death = new DeathSnapshot(
                this.Level,
                this.Position,
                this.HeadY - 0.3,
                Math.Min(this.XpLevel * 7, 100),
                this.Level.LevelData.GetBooleanRule("keep_inventory", defaultValue: false)
                    || this.GameMode == GameMode.Spectator,
                this.OpenedContainer ?? this.Inventory,
                this.CurrentContainerId);
            finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.deathFinished = finished;
            this.Health = 0;
            transfers = AttachmentStorage.For(this).BeginDeath();
        }

        ValueTask Complete() => this.CompleteDeathAsync(source, message, death, transfers, finished);

        // Block hooks can synchronously kill a player on the tick lock. Never wait for a click from that lock.
        if (death.Level is AbstractLevel level && level.Fluids.TickLock.IsHeldByCurrentThread)
        {
            level.EnqueueEntityAction(Complete);
            return ValueTask.CompletedTask;
        }

        return DeferredInventoryWork.RunOrDeferAsync(Complete);
    }

    private async ValueTask KillAfterRespawnAsync(Task respawn, IEntity source, ChatMessage message)
    {
        await respawn;
        await this.BeginDeathAsync(source, message);
    }

    private async ValueTask CompleteDeathAsync(
        IEntity source,
        ChatMessage message,
        DeathSnapshot death,
        Task transfers,
        TaskCompletionSource finished)
    {
        // A close/packet callback may request respawn. It joins this queue, after the death barrier is signalled.
        await using var callbacks = DeferredInventoryWork.Begin(this.Logger, () => finished.TrySetResult());

        try
        {
            this.CancelEating();
            this.CancelWeaponUse();
            this.LeaveVehicle();

            await transfers;
            await this.LoseItemsAtDeathAsync(death);
            await this.RecordProgressDeathAsync(source);
            await this.Client.QueuePacketAsync(new SetHealthPacket(0, this.FoodLevel, this.FoodSaturationLevel));
            await this.Client.QueuePacketAsync(new PlayerCombatKillPacket
            {
                PlayerID = this.EntityId,
                Message = message
            });
            this.PacketBroadcaster.QueuePacketToLevel(death.Level, new EntityEventPacket
            {
                EntityId = this.EntityId,
                Event = 3
            }, this.EntityId);

            foreach (var observer in death.Level.GetPlayersInRange(death.Position, float.MaxValue).OfType<Player>())
                observer.visiblePlayers.Remove(this);
        }
        catch (Exception exception)
        {
            finished.TrySetException(exception);
            throw;
        }
    }

    /// <summary>
    /// Reclaims the dead player's transient slots and inventory in one window transaction. The close event only
    /// notifies afterwards: cancellation or a failed handler cannot prevent recovery or move the contents twice.
    /// </summary>
    private async ValueTask LoseItemsAtDeathAsync(DeathSnapshot death)
    {
        var drops = new List<ItemStack>();
        var window = death.Window;
        var windowId = death.WindowId;

        using (window is MerchantContainer merchant ? await merchant.LockDeathAsync() : null)
        using (await BaseContainer.LockWindowAsync(window, this.Inventory))
        {
            Take(this.Inventory, 1, 5);
            this.Inventory.RemoveItem(0);

            // ServerPlayer.die removes its menu with the dead player. AbstractContainerMenu.removed and
            // clearContainer drop carried/transient items even when keep_inventory is enabled.
            switch (window)
            {
                case CraftingTable { Type: InventoryType.Crafting }:
                    Take(window, 1, 10);
                    window.RemoveItem(0);
                    break;
                case EnchantmentTable:
                    Take(window, 0, 2);
                    break;
                case AnvilContainer or CartographyTable or Grindstone or MerchantContainer:
                    Take(window, 0, 2);
                    window.RemoveItem(2);
                    break;
                case Loom:
                    Take(window, 0, 3);
                    window.RemoveItem(3);
                    break;
                case Stonecutter:
                    Take(window, 0, 1);
                    window.RemoveItem(1);
                    break;
            }

            if (this.CarriedItem is { Count: > 0 } cursor)
                drops.Add(new ItemStack(cursor, cursor.Count));

            this.CarriedItem = null;
            this.DraggedSlots.Clear();
            this.IsDragging = false;

            if (!death.Keeps)
            {
                foreach (var slot in deathDropSlots)
                {
                    if (this.Inventory.GetItem(slot) is not ItemStack item)
                        continue;

                    this.Inventory.RemoveItem(slot);

                    if (item.Count > 0 && !Vanishes(item))
                        drops.Add(new ItemStack(item, item.Count));
                }
            }

            if (ReferenceEquals(this.OpenedContainer, window))
                this.OpenedContainer = null;
        }

        if (!death.Keeps)
        {
            drops.AddRange(AttachmentStorage.For(this).TakeDeathDrops(this).Where(item => !Vanishes(item)));
            this.XpLevel = 0;
            this.XpTotal = 0;
            this.XpP = 0;
        }

        foreach (var item in drops)
            ItemEntity.DropAround(death.Level, death.Position, death.DropY, item);

        if (!death.Keeps && death.Level is AbstractLevel level)
            DropExperience(level, death.Position, death.Experience);

        // No packets, event handlers or attachment notifications execute under the window's locks.
        if (window != this.Inventory)
        {
            await this.EventDispatcher.ExecuteEventAsync(
                new ContainerClosedEventArgs(this, this.Server, window, windowId));
            await this.Client.QueuePacketAsync(new ContainerClosePacket { ContainerId = windowId });
        }

        this.RunMenusClosed();

        await MainEventHandler.RefreshWindowAsync(this, this.Inventory, stateId: 0);

        void Take(BaseContainer container, int start, int end)
        {
            for (var slot = start; slot < end; slot++)
            {
                if (container.GetItem(slot) is { Count: > 0 } item)
                    drops.Add(new ItemStack(item, item.Count));

                container.RemoveItem(slot);
            }
        }
    }

    // Vanilla's Player.getBaseExperienceReward and ExperienceOrb.award, using the death-time level and position.
    private static void DropExperience(AbstractLevel level, VectorD position, int remaining)
    {
        while (remaining > 0)
        {
            var value = ExperienceOrb.SplitValue(remaining);
            level.TrySpawnEntity(new ExperienceOrb
            {
                Level = level,
                EntityId = Obsidian.Server.GetNextEntityId(),
                Position = position,
                Value = value
            }, hostOwned: true);
            remaining -= value;
        }
    }
}
