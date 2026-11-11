using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Dispensing;

namespace Obsidian.WorldData.TrialSpawners;

internal sealed partial class VaultBlockEntity
{
    private const string StateProperty = "vault_state";

    // Vanilla leaves failure-sound throttling transient; shared changes from a use are sent on the next tick.
    private long lastInsertFailure;
    private bool sharedChanged;
    private bool pendingUseStateUpdate;

    protected internal override ValueTask TickAsync()
    {
        if (this.Level is AbstractLevel level)
        {
            lock (level.Fluids.TickLock)
            {
                this.Tick(level);
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Tick(AbstractLevel level)
    {
        var block = level.BlockUpdates.GetBlock(this.BlockPosition);
        if (!block.Is(BlockIds.Vault))
            return;

        if (this.pendingUseStateUpdate)
        {
            // Vanilla broadcasts the use's block-state change at the start of the next level tick, before the
            // vault ticker sends its dirty shared data. Keep both packets even when their tags are identical.
            var packet = new BlockEntityDataPacket
            {
                Position = this.BlockPosition,
                Type = BlockEntityTypesRegistry.NetworkId(this.Id),
                NBTData = this.GetUpdateData()!
            };
            foreach (var player in level.PlayersInRange(this.BlockPosition))
                player.SendPacket(packet);

            this.pendingUseStateUpdate = false;
        }

        var state = block.GetProperty(StateProperty);
        var time = level.LevelData.GameTime;
        if (state == "active" && time % 20 == 0)
            this.CycleDisplay(level);

        if (time >= this.Data.Server.StateUpdatingResumesAt)
        {
            if (state == "unlocking")
            {
                this.PauseUntil(time + 20);
                this.Transition(level, block, "ejecting");
            }
            else if (state == "ejecting" && !this.Data.Server.ItemsToEject.IsEmpty)
            {
                this.Eject(level);
                this.PauseUntil(time + 20);
            }
            else
            {
                var range = state == "inactive" ? this.Data.Config.ActivationRange : this.Data.Config.DeactivationRange;
                if (state == "ejecting")
                    this.Data = this.Data with { Server = this.Data.Server with { TotalEjectionsNeeded = 0 } };

                this.ConnectPlayers(level, range);
                this.PauseUntil(time + 20);
                this.Transition(level, block, this.Data.Shared.ConnectedPlayers.IsEmpty ? "inactive" : "active");
            }
        }

        if (this.sharedChanged)
        {
            this.SendUpdate();
            this.sharedChanged = false;
        }
    }

    /// <summary>
    /// Attempts a claim during a block-use commit, holding the tick and hand inventory locks. Loot evaluation and
    /// notifications never acquire the entity transfer lock; block-use effects publish after the hand unlocks.
    /// </summary>
    internal bool TryUnlock(BlockUse use, out bool rewarded)
    {
        rewarded = false;
        var config = this.Data.Config;
        if (config.KeyItem is not { IsAir: false, Count: > 0 } key)
            return true;

        var time = use.Level.LevelData.GameTime;
        string? failure = null;
        if (use.Item is not ItemStack held || !held.Equals(key) || held.Count < key.Count)
            failure = SoundId.Block.VaultInsertItemFail;
        else if (this.Data.Server.RewardedPlayers.Contains(use.Player.Uuid))
            failure = SoundId.Block.VaultRejectRewardedPlayer;

        if (failure is not null)
        {
            if (time >= this.lastInsertFailure + 15)
            {
                use.Level.BlockUpdates.PlaySound(this.BlockPosition, "minecraft:" + failure);
                this.lastInsertFailure = time;
            }

            return true;
        }

        var items = this.RollLoot(use.Level, config.LootTable, use.Player, use.Item);
        if (items.Count == 0)
            return true;

        var players = this.Data.Server.RewardedPlayers.Add(use.Player.Uuid);
        if (players.Length > 128)
            players = players.RemoveAt(0);

        // Saves see detached stacks, never a hand or an item entity's mutable stack.
        this.Data = this.Data with
        {
            Server = this.Data.Server with
            {
                ItemsToEject = [.. items.Select(item => new ItemStack(item, item.Count))],
                TotalEjectionsNeeded = items.Count,
                StateUpdatingResumesAt = time + 14,
                RewardedPlayers = players
            }
        };
        this.SetDisplay(items[^1]);
        this.Transition(use.Level, use.Block, "unlocking");
        this.ConnectPlayers(use.Level, config.DeactivationRange);
        this.SetChanged();

        if (use.Player.GameMode != GameMode.Creative)
        {
            use.Player.Inventory.RemoveItem(use.HandSlot, key.Count);
            use.ResyncHand = true;
            use.ChangedInventorySlots.Add(use.HandSlot);
        }

        rewarded = true;
        return true;
    }

    private void PauseUntil(long time)
    {
        this.Data = this.Data with { Server = this.Data.Server with { StateUpdatingResumesAt = time } };
        this.SetChanged();
    }

    private void ConnectPlayers(AbstractLevel level, double range)
    {
        var found = new List<Guid>();
        foreach (var player in level.Players.Values)
        {
            if (player.GameMode == GameMode.Spectator || this.Data.Server.RewardedPlayers.Contains(player.Uuid))
                continue;

            // Vanilla's detector compares block positions strictly within the range, without a sight-line check.
            var offset = (Vector)player.Position.Floor() - this.BlockPosition;
            var distance = (double)offset.X * offset.X + (double)offset.Y * offset.Y + (double)offset.Z * offset.Z;
            if (distance < range * range)
                found.Add(player.Uuid);
        }

        var previous = this.Data.Shared.ConnectedPlayers;
        if (previous.Length == found.Count && found.All(previous.Contains))
            return;

        // The detector collects a new Java HashSet; loaded shared data retains its saved order until it changes.
        var ordered = UuidSet.Of(found).InIterationOrder.ToImmutableArray();
        this.Data = this.Data with { Shared = this.Data.Shared with { ConnectedPlayers = ordered } };
        this.sharedChanged = true;
        this.SetChanged();
    }

    private void CycleDisplay(AbstractLevel level)
    {
        if (this.Data.Config.KeyItem is not { IsAir: false, Count: > 0 })
        {
            this.SetDisplay(null);
            return;
        }

        var config = this.Data.Config;
        var items = this.RollLoot(level, config.OverrideLootTableToDisplay ?? config.LootTable);
        this.SetDisplay(items.Count == 0 ? null : items[level.GrowthRandom.NextInt(items.Count)]);
    }

    private List<ItemStack> RollLoot(AbstractLevel level, string tableId, PlayerBase? player = null,
        ItemStack? tool = null)
    {
        var resolver = PluginLootTables.CaptureResolver();
        var table = resolver(tableId) ?? LootTable.Empty;
        // Displays explicitly use the level random. Claims use the loot table's named sequence, when present.
        var random = player is not null && table.RandomSequence is string sequence
            ? BlockLoot.SequencesFor(level).Get(sequence)
            : level.GrowthRandom;
        var luck = player is not null && player.HasAttribute("minecraft:luck")
            ? player.GetAttributeValue("minecraft:luck")
            : 0;
        lock (random)
        {
            return table.GetRandomItems(new LootContext
            {
                Random = random,
                Origin = (VectorD)this.BlockPosition + 0.5,
                ThisEntity = player,
                Tool = tool,
                Luck = luck,
                EntityVillagerVariant = LootEntityComponents.VillagerVariant,
                ResolveTable = resolver
            });
        }
    }

    private void SetDisplay(ItemStack? item)
    {
        var current = this.Data.Shared.DisplayItem;
        if (current is null && item is null || current is not null && item is not null
            && current.Count == item.Count && current.Equals(item))
        {
            return;
        }

        var copy = item is null ? null : new ItemStack(item, item.Count);
        this.Data = this.Data with { Shared = this.Data.Shared with { DisplayItem = copy } };
        this.sharedChanged = true;
        this.SetChanged();
    }

    private void Transition(AbstractLevel level, IBlock block, string next)
    {
        var state = block.GetProperty(StateProperty);
        if (state == next)
            return;

        level.BlockUpdates.SetBlock(this.BlockPosition, block.WithProperty(StateProperty, next));
        if (level.ActiveBlockUse is not null)
            this.pendingUseStateUpdate = true;
        else
            this.SendUpdate();

        if (state == "ejecting")
            level.BlockUpdates.PlaySound(this.BlockPosition, "minecraft:" + SoundId.Block.VaultCloseShutter);

        if (next is "inactive" or "active")
        {
            if (next == "inactive")
                this.SetDisplay(null);
            else if (this.Data.Shared.DisplayItem is null)
                this.CycleDisplay(level);

            var ominous = block.GetProperty("ominous") == "true" ? 1 : 0;
            level.BroadcastLevelEvent(next == "active" ? 3015 : 3016, this.BlockPosition, ominous);
        }
        else
        {
            var sound = next == "unlocking" ? SoundId.Block.VaultInsertItem : SoundId.Block.VaultOpenShutter;
            level.BlockUpdates.PlaySound(this.BlockPosition, "minecraft:" + sound);
        }
    }

    private void Eject(AbstractLevel level)
    {
        var server = this.Data.Server;
        // Vanilla removes from the end, and computes pitch before removing the stack in single precision.
        var progress = server.TotalEjectionsNeeded == 1
            ? 1f
            : 1f - (server.ItemsToEject.Length - 1f) / (server.TotalEjectionsNeeded - 1f);
        var item = server.ItemsToEject[^1];
        var remaining = server.ItemsToEject.RemoveAt(server.ItemsToEject.Length - 1);
        this.Data = this.Data with { Server = server with { ItemsToEject = remaining } };
        var from = (VectorD)this.BlockPosition + new VectorD(0.5, 1.2, 0.5);
        DispenseBehaviors.SpawnItem(level, new ItemStack(item, item.Count), 2, BlockFace.Up, from);
        level.BroadcastLevelEvent(3017, this.BlockPosition, 0);
        level.BlockUpdates.PlaySound(this.BlockPosition, "minecraft:" + SoundId.Block.VaultEjectItem,
            1f, 0.8f + 0.4f * progress);
        this.SetDisplay(remaining.IsEmpty ? null : remaining[^1]);
    }
}
