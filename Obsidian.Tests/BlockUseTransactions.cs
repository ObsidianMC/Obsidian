using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Collections.Generic;
using Obsidian.Net.Packets.Play.Clientbound;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class BlockUseTransactions
{
    private static readonly Vector target = new(8, 64, 8);
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task NeighborPluginsSeeTheCommittedExchangeAndCanChangeTheHand()
    {
        await using var context = TestServerFactory.CreateServer($"exchange-neighbors-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(target);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "neighbor.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        Assert.NotNull(await manager.LoadPluginAsync(archive));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:relay", out var relay));
        world.BlockUpdates.SetBlock(target + Vector.East, relay.DefaultState, Quiet);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Jukebox, Quiet);
        var player = Player(context, "User");
        player.Inventory.SetItem(player.CurrentHeldItemSlot,
            new ItemStack(ItemsRegistry.Get("minecraft:music_disc_cat"), 2));
        var observedCommitted = false;
        var changedHand = false;

        AppContext.SetData(ProbeRelay.NeighborHookKey, (Action<NeighborChangedContext>)(neighbor =>
        {
            if (neighbor.SourcePosition != target || changedHand)
                return;

            observedCommitted = player.GetHeldItem()?.Count == 1
                && world.BlockUpdates.GetBlock(target).GetProperty("has_record") == "true"
                && Stored(context).Count == 1;
            if (player.Inventory.TryLock(out var inventoryLock))
            {
                using (inventoryLock)
                    player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Diamond, 3));

                changedHand = true;
            }
        }));
        try
        {
            var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
            await packet.UseAsync(context.Server, player).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(observedCommitted);
            Assert.True(changedHand);
            Assert.Equal(ItemsRegistry.Diamond.Id, player.GetHeldItem()?.Holder.Id);
            Assert.Equal(3, player.GetHeldItem()?.Count);
            Assert.Equal(1, Stored(context).Count);
        }
        finally
        {
            AppContext.SetData(ProbeRelay.NeighborHookKey, null);
        }
    }

    [Fact]
    public async Task BoneMealFeaturesSeeTheConsumedItemAndCanChangeTheUnlockedHand()
    {
        await using var context = TestServerFactory.CreateServer($"bone-meal-feature-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(target);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "growth.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        Assert.NotNull(await manager.LoadPluginAsync(archive));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:sapling", out var sapling));
        world.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Dirt, Quiet);
        world.BlockUpdates.SetBlock(target, sapling.DefaultState.WithProperty("stage", "1"), Quiet);
        // The sapling's check draws its tree first; the chance after it comes up with this seed.
        world.GrowthRandom = new LegacyRandomSource(1);
        var player = Player(context, "User");
        var boneMeal = new ItemStack(ItemsRegistry.BoneMeal, 2);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, boneMeal);
        var observedConsumed = false;
        var changedHand = false;
        AppContext.SetData(ProbePillarFeature.PlaceHookKey, (Action<FeatureContext>)(_ =>
        {
            observedConsumed = boneMeal.Count == 1;
            if (player.Inventory.TryLock(out var inventoryLock))
            {
                using (inventoryLock)
                    player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Diamond, 3));

                changedHand = true;
            }
        }));
        try
        {
            await BlockUpdates.UseOn(target, BlockFace.Up).HandleAsync(context.Server, player);
            await world.RunQueuedEntityActionsAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(observedConsumed);
            Assert.True(changedHand);
            Assert.Equal(1, boneMeal.Count);
            Assert.Equal(ItemsRegistry.Diamond.Id, player.GetHeldItem()?.Holder.Id);
            Assert.Equal(3, player.GetHeldItem()?.Count);
            Assert.Equal(1, player.Statistics.Get("minecraft:bone_meal", "minecraft:used"));
        }
        finally
        {
            AppContext.SetData(ProbePillarFeature.PlaceHookKey, null);
        }
    }

    [Fact]
    public async Task PartialChangesAndFailingEffectsStillPublishEveryStoredWrite()
    {
        await using var context = TestServerFactory.CreateServer($"exchange-faults-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(target);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "fault-neighbor.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        Assert.NotNull(await manager.LoadPluginAsync(archive));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:relay", out var relay));
        var other = target + Vector.East + Vector.North;
        world.BlockUpdates.SetBlock(target + Vector.East, relay.DefaultState, Quiet);
        var player = Player(context, "User");
        var client = (TestClient)player.Client;
        Assert.True(context.Server.AddPlayer(player));
        player.LoadedChunks.Add(0);
        var notified = new HashSet<Vector>();
        AppContext.SetData(ProbeRelay.NeighborHookKey,
            (Action<NeighborChangedContext>)(neighbor => notified.Add(neighbor.SourcePosition)));
        try
        {
            foreach (var fault in new[] { "change", "effect", "both" })
            {
                world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone, Quiet);
                world.BlockUpdates.SetBlock(other, BlocksRegistry.Stone, Quiet);
                var stack = new ItemStack(ItemsRegistry.BoneMeal, 2);
                player.Inventory.SetItem(player.CurrentHeldItemSlot, stack);
                var use = Read(context, player);
                var changeFailure = new InvalidOperationException("Injected change failure");
                var effectFailure = new InvalidOperationException("Injected publication failure");
                notified.Clear();
                client.ClearPackets();

                var failure = Assert.Throws<InvalidOperationException>(() => use.TryCommit(() =>
                {
                    if (fault is "effect" or "both")
                        world.ActiveBlockUse!.After(() => throw effectFailure);

                    world.BlockUpdates.SetBlock(target, BlocksRegistry.Dirt);
                    world.BlockUpdates.SetBlock(other, BlocksRegistry.Cobblestone);
                    if (fault is "change" or "both")
                        throw changeFailure;

                    return true;
                }, consumeItem: true));

                Assert.Same(fault == "effect" ? effectFailure : changeFailure, failure);
                Assert.Same(stack, player.GetHeldItem());
                Assert.Equal(fault == "effect" ? 1 : 2, stack.Count);
                Assert.Contains(target, notified);
                Assert.Contains(other, notified);
                Assert.Contains(client.SentPackets.OfType<BlockUpdatePacket>(),
                    packet => packet.Position == target && packet.BlockId == BlocksRegistry.Dirt.GetHashCode());
                Assert.Contains(client.SentPackets.OfType<BlockUpdatePacket>(),
                    packet => packet.Position == other && packet.BlockId == BlocksRegistry.Cobblestone.GetHashCode());

                // A caller can recover: a later exchange consumes once and publishes its own visible effects.
                notified.Clear();
                client.ClearPackets();
                var recovery = Read(context, player);
                Assert.True(recovery.TryCommit(() =>
                {
                    world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
                    return true;
                }, consumeItem: true));
                Assert.Equal(fault == "effect" ? 0 : 1, player.GetHeldItem()?.Count ?? 0);
                Assert.Contains(target, notified);
                Assert.Contains(client.SentPackets.OfType<BlockUpdatePacket>(),
                    packet => packet.Position == target && packet.BlockId == BlocksRegistry.Stone.GetHashCode());
            }
        }
        finally
        {
            AppContext.SetData(ProbeRelay.NeighborHookKey, null);
        }
    }

    [Fact]
    public async Task CompetingInsertionsKeepTheLosingItemAndCannotOverwriteTheWinner()
    {
        await using var context = TestServerFactory.CreateServer($"block-exchange-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var alice = Player(context, "Alice");
        var bob = Player(context, "Bob");

        foreach (var (block, item) in new[]
        {
            (BlocksRegistry.Jukebox, ItemsRegistry.Get("minecraft:music_disc_cat")),
            (BlocksRegistry.Lectern, ItemsRegistry.WritableBook)
        })
        {
            context.World.BlockUpdates.SetBlock(target, block, Quiet);
            alice.Inventory.SetItem(alice.CurrentHeldItemSlot, new ItemStack(item, 1));
            bob.Inventory.SetItem(bob.CurrentHeldItemSlot, new ItemStack(item, 1));
            var first = Read(context, alice);
            var second = Read(context, bob);
            var behavior = BlocksRegistry.GetUseItemOnBehavior(block.RegistryId)!;

            Assert.Equal(UseResult.Success, await behavior.UseItemOnAsync(first));
            Assert.False((await behavior.UseItemOnAsync(second)).ConsumesAction());

            Assert.Null(alice.GetHeldItem());
            Assert.Equal(1, bob.GetHeldItem()?.Count);
            Assert.Equal(1, Stored(context).Count);
            Assert.Equal(item.Id, Stored(context).Holder.Id);

            if (block.RegistryId == BlockIds.Jukebox)
            {
                // Two ejections that read the same full jukebox release exactly one disc.
                bob.Inventory.SetItem(bob.CurrentHeldItemSlot, null);
                var ejectFirst = Read(context, bob);
                var ejectSecond = Read(context, bob);
                Assert.Equal(UseResult.Success, await behavior.UseWithoutItemAsync(ejectFirst));
                Assert.False((await behavior.UseWithoutItemAsync(ejectSecond)).ConsumesAction());
                Assert.Single(context.World.GetEntitiesInRange((VectorD)target, 4).OfType<ItemEntity>());
            }
        }
    }

    [Fact]
    public async Task InterruptedInsertionsPreserveBothTheHandAndTheDestination()
    {
        await using var context = TestServerFactory.CreateServer($"stale-exchange-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var player = Player(context, "User");
        var slot = player.CurrentHeldItemSlot;

        foreach (var interference in new[] { "hand", "entity", "block", "death", "respawn", "busy" })
        {
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Jukebox, Quiet);
            player.Health = 20;
            player.Respawning = false;
            var disc = new ItemStack(ItemsRegistry.Get("minecraft:music_disc_cat"), 1);
            player.Inventory.SetItem(slot, disc);
            var use = Read(context, player);
            IDisposable heldLock = null;
            switch (interference)
            {
                case "hand":
                    player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Diamond, 2));
                    break;
                case "entity":
                    context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
                    context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Jukebox, Quiet);
                    break;
                case "block":
                    context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Stone, Quiet);
                    break;
                case "death":
                    player.Health = 0;
                    break;
                case "respawn":
                    player.Respawning = true;
                    break;
                case "busy":
                    heldLock = player.Inventory.Lock();
                    break;
            }

            try
            {
                var behavior = BlocksRegistry.GetUseItemOnBehavior(BlockIds.Jukebox)!;
                Assert.False((await behavior.UseItemOnAsync(use)).ConsumesAction(), interference);
            }
            finally
            {
                heldLock?.Dispose();
            }

            Assert.Equal(1, disc.Count);
            if (interference == "hand")
            {
                Assert.Equal(ItemsRegistry.Diamond.Id, player.GetHeldItem()?.Holder.Id);
                Assert.Equal(2, player.GetHeldItem()?.Count);
            }
            else
            {
                Assert.Same(disc, player.GetHeldItem());
            }

            if (context.World.GetResidentBlockEntity(target) is DataBlockEntity jukebox)
                Assert.False(jukebox.SnapshotData().HasTag("RecordItem"), interference);
        }
    }

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        return player;
    }

    internal static BlockUse Read(RealServerContext context, ServerPlayer player) => new()
    {
        Server = context.Server,
        Level = context.World,
        Player = player,
        Position = target,
        Block = context.World.BlockUpdates.GetBlock(target),
        BlockEntity = context.World.GetResidentBlockEntity(target),
        Face = BlockFace.Up,
        Cursor = new VectorD(0.5, 0.5, 0.5),
        Hand = InteractionHand.MainHand,
        HandSlot = player.CurrentHeldItemSlot,
        Item = player.GetHeldItem(),
        UsedItem = player.GetHeldItem() is ItemStack item ? new ItemStack(item, item.Count) : null,
        Sequence = 1
    };

    private static ItemStack Stored(RealServerContext context)
    {
        var entity = context.World.GetResidentBlockEntity(target);
        if (entity is LecternBlockEntity lectern)
            return Assert.IsType<ItemStack>(lectern.Lectern.Book);

        var jukebox = Assert.IsType<DataBlockEntity>(entity);
        Assert.True(jukebox.SnapshotData().TryGetTag<NbtCompound>("RecordItem", out var record));
        return record.ItemFromNbt();
    }
}
