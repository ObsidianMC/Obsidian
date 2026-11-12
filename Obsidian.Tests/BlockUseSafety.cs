using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Events;
using Obsidian.WorldData;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Utilities.Collections;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BlockUseSafety
{
    [Fact]
    public async Task AClickWaitsForItsHandSnapshotAndRefusalsRepairPredictionBeforeAcknowledging()
    {
        await using var context = TestServerFactory.CreateServer($"busy-use-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Chest);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        var client = (TestClient)player.Client;
        var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));

        Task<UseResult?> click;
        using (await player.Inventory.LockAsync())
        {
            click = packet.UseAsync(context.Server, player).AsTask();
            Assert.False(click.IsCompleted);
            Assert.DoesNotContain(client.SentPackets, sent => sent is BlockChangedAckPacket);
        }

        await click.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(player.OpenedContainer);
        player.FlushBlockAcknowledgement();
        await player.CloseContainerAsync();
        client.ClearPackets();
        packet = BlockUpdates.UseOn(target, BlockFace.Up, sequence: 2);
        using (await player.Inventory.LockAsync())
        {
            click = packet.UseAsync(context.Server, player).AsTask();
            Assert.False(click.IsCompleted);
            await player.SendPositionAsync(VectorD.Zero);
        }

        Assert.Null(await click.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(player.OpenedContainer);
        Assert.Equal(2, client.SentPackets.OfType<BlockUpdatePacket>().Count());
        Assert.Contains(client.SentPackets, sent => sent is ContainerSetSlotPacket);
        Assert.DoesNotContain(client.SentPackets, sent => sent is BlockChangedAckPacket);
        player.FlushBlockAcknowledgement();
        Assert.IsType<BlockChangedAckPacket>(client.SentPackets.Last());
        await client.ConfirmTeleportAsync();
        var sequence = 2;

        // Early refusals and an item's refused placement all repair both cells and the hand first.
        foreach (var refusal in new[] { "dead", "reach", "placement" })
        {
            client.ClearPackets();
            player.Health = refusal == "dead" ? 0 : 20;
            player.Position = refusal == "reach" ? new VectorD(100, 64, 100) : new VectorD(8.5, 64, 10.5);
            world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
            world.BlockUpdates.SetBlock(target + Vector.Up, BlocksRegistry.Stone);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Stone, 1));

            packet = BlockUpdates.UseOn(target, BlockFace.Up, sequence: ++sequence);
            await packet.UseAsync(context.Server, player);
            player.FlushBlockAcknowledgement();

            var packets = client.SentPackets.ToArray();
            var ack = Array.FindIndex(packets, sent => sent is BlockChangedAckPacket);
            var slot = Array.FindIndex(packets, sent => sent is ContainerSetSlotPacket);
            Assert.Equal(2, packets.Take(ack).OfType<BlockUpdatePacket>().Count());
            Assert.True(slot >= 0 && slot < ack, refusal);
            Assert.IsType<BlockChangedAckPacket>(packets.Last());
        }
    }

    [Fact]
    public async Task PooledDeferredClicksKeepTheirCorrectionsAndAcknowledgeTheHighestReceivedSequence()
    {
        await using var context = TestServerFactory.CreateServer($"pooled-use-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        var later = target + Vector.East;
        await world.GetBlockAsync(target);
        var pool = new SimpleObjectPool<UseItemOnPacket>(1);

        foreach (var acknowledgeBeforeDrain in new[] { false, true })
        {
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
                new VectorD(8.5, 64, 10.5));
            world.TryAddPlayer(player);
            player.GameMode = GameMode.Survival;
            var client = (TestClient)player.Client;
            world.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Farmland);
            world.BlockUpdates.SetBlock(target, BlocksRegistry.Wheat);
            world.BlockUpdates.SetBlock(later, BlocksRegistry.Stone);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.BoneMeal, 2));
            client.ClearPackets();

            var first = pool.Get();
            BlockUpdates.UseOn(target, BlockFace.Up, sequence: 11, packet: first);
            await first.HandleAsync(context.Server, player);
            pool.Return(first);

            var reused = pool.Get();
            Assert.Same(first, reused);
            BlockUpdates.UseOn(later, BlockFace.East, sequence: 12, packet: reused);
            await reused.HandleAsync(context.Server, player);
            pool.Return(reused);
            Assert.Empty(client.SentPackets.OfType<BlockChangedAckPacket>());
            if (acknowledgeBeforeDrain)
                world.FlushBlockChanges();

            var beforeDrain = client.SentPackets.Count;
            await world.RunQueuedEntityActionsAsync();
            var corrections = client.SentPackets.Skip(beforeDrain).ToArray();
            Assert.Equal(new[] { target, target + Vector.Up },
                corrections.OfType<BlockUpdatePacket>().Select(packet => packet.Position));
            Assert.Contains(corrections, packet => packet is ContainerSetSlotPacket);
            Assert.Equal(1, player.GetHeldItem()?.Count);

            world.FlushBlockChanges();
            Assert.Equal(12, Assert.Single(client.SentPackets.OfType<BlockChangedAckPacket>()).SequenceID);
            if (!acknowledgeBeforeDrain)
                Assert.IsType<BlockChangedAckPacket>(client.SentPackets.Last());

            // Another flush can acknowledge a newer sequence, never a late completion's older one.
            await BlockUpdates.UseOn(later, BlockFace.Up, sequence: 13).HandleAsync(context.Server, player);
            world.FlushBlockChanges();
            Assert.Equal(new[] { 12, 13 },
                client.SentPackets.OfType<BlockChangedAckPacket>().Select(packet => packet.SequenceID));
            world.TryRemovePlayer(player);
        }
    }

    [Fact]
    public async Task QueuedBoneMealCannotCommitWhileATeleportIsPending()
    {
        await using var context = TestServerFactory.CreateServer($"pending-bone-meal-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        world.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Farmland);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Wheat);
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "Gardener", new VectorD(8.5, 64, 10.5));
        Assert.True(world.TryAddPlayer(player));
        player.GameMode = GameMode.Survival;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.BoneMeal, 2));
        var client = (TestClient)player.Client;
        var use = BlockUpdates.UseOn(target, BlockFace.Up, sequence: 81);
        Assert.Equal(UseResult.SuccessServer, await use.UseAsync(context.Server, player));
        await player.SendPositionAsync(VectorD.Zero);
        client.ClearPackets();

        await world.RunQueuedEntityActionsAsync();

        Assert.True(world.BlockUpdates.GetBlock(target).IsSameState(BlocksRegistry.Wheat));
        Assert.Equal(2, player.GetHeldItem()!.Count);
        Assert.Equal(0, player.Statistics.Get("minecraft:bone_meal", type: "minecraft:used"));
        Assert.Equal(2, client.SentPackets.OfType<BlockUpdatePacket>().Count());
        Assert.Contains(client.SentPackets, sent => sent is ContainerSetSlotPacket);
        Assert.DoesNotContain(client.SentPackets, sent => sent is BlockChangedAckPacket);
        player.FlushBlockAcknowledgement();
        Assert.Equal(81, Assert.Single(client.SentPackets.OfType<BlockChangedAckPacket>()).SequenceID);
    }

    [Fact]
    public async Task StackedFishBucketsCannotOverwriteTheHandOrPlaceAMob()
    {
        await using var context = TestServerFactory.CreateServer($"stacked-buckets-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        var buckets = new ItemStack(ItemsRegistry.Get("minecraft:cod_bucket"), 2);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, buckets);

        var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
        Assert.Equal(UseResult.Fail, await packet.UseAsync(context.Server, player));

        Assert.Same(buckets, player.GetHeldItem());
        Assert.Equal(2, buckets.Count);
        Assert.True(world.BlockUpdates.GetBlock(target + Vector.Up).IsAir);
        Assert.Empty(world.GetEntitiesInRange((VectorD)target, 4).Where(entity => !ReferenceEquals(entity, player)));
    }

    [Fact]
    public async Task DeadOrRespawningClicksAreAcknowledgedWithoutUsingItems()
    {
        await using var context = TestServerFactory.CreateServer($"dead-block-use-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.SetBlockAsync(target, BlocksRegistry.Get("minecraft:obsidian"));
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        var crystals = new ItemStack(ItemsRegistry.EndCrystal, 2);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, crystals);

        foreach (var respawning in new[] { false, true })
        {
            player.Health = respawning ? 20 : 0;
            player.Respawning = respawning;
            ((TestClient)player.Client).ClearPackets();

            var packet = BlockUpdates.UseOn(target, BlockFace.Up, sequence: respawning ? 2 : 1);
            Assert.Null(await packet.UseAsync(context.Server, player));
            player.FlushBlockAcknowledgement();

            Assert.Equal(2, crystals.Count);
            Assert.Empty(world.GetEntitiesInRange((VectorD)target, 8).Where(entity => !ReferenceEquals(entity, player)));
            Assert.Single(((TestClient)player.Client).SentPackets.OfType<BlockChangedAckPacket>());
        }
    }

    [Fact]
    public async Task CancelledLecternInsertionKeepsTheBookAndDoesNotOpenAMenu()
    {
        await using var context = TestServerFactory.CreateServer($"cancel-book-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var target = new Vector(8, 64, 8);
        await context.World.GetBlockAsync(target);
        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Lectern);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "User",
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        var book = new ItemStack(ItemsRegistry.WritableBook, 1);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, book);
        var requested = false;
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerInteractEventArgs, ValueTask>)(interaction =>
        {
            requested = true;
            interaction.Cancel();
            return ValueTask.CompletedTask;
        }));

        var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
        Assert.Equal(UseResult.Fail, await packet.UseAsync(context.Server, player));

        Assert.True(requested);
        Assert.Same(book, player.GetHeldItem());
        Assert.Equal(1, book.Count);
        Assert.Null(player.OpenedContainer);
        var lectern = Assert.IsType<LecternBlockEntity>(context.World.GetResidentBlockEntity(target));
        Assert.Null(lectern.Lectern.Book);
        Assert.Equal("false", context.World.BlockUpdates.GetBlock(target).GetProperty("has_book"));
    }

    [Fact]
    public async Task LecternHooksCannotCommitAfterTheHandBlockOrPlayerChanges()
    {
        await using var context = TestServerFactory.CreateServer($"lectern-hooks-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        var interference = "hand";
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerInteractEventArgs, ValueTask>)(async _ =>
        {
            await Task.Yield();
            switch (interference)
            {
                case "hand":
                    using (await player.Inventory.LockAsync())
                        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Diamond, 2));
                    break;
                case "block":
                    world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
                    break;
                case "death":
                    player.Health = 0;
                    break;
                case "count":
                    player.GetHeldItem()!.Count++;
                    break;
            }
        }));

        foreach (var changed in new[] { "hand", "block", "death", "count" })
        {
            interference = changed;
            player.Health = 20;
            world.BlockUpdates.SetBlock(target, BlocksRegistry.Lectern);
            var book = new ItemStack(ItemsRegistry.WritableBook, 1);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, book);

            var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
            Assert.Equal(UseResult.Fail, await packet.UseAsync(context.Server, player));

            Assert.Equal(changed == "count" ? 2 : 1, book.Count);
            Assert.Null(player.OpenedContainer);
            if (world.GetResidentBlockEntity(target) is LecternBlockEntity lectern)
                Assert.Null(lectern.Lectern.Book);

            if (changed == "hand")
            {
                Assert.Equal(ItemsRegistry.Diamond.Id, player.GetHeldItem()?.Holder.Id);
                Assert.Equal(2, player.GetHeldItem()?.Count);
            }
        }
    }

    [Fact]
    public async Task QueuedBoneMealClicksRewardOnlyCommittedItems()
    {
        await using var context = TestServerFactory.CreateServer($"queued-bone-meal-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        var watcher = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Watcher",
            new VectorD(10, 64, 10));
        world.TryAddPlayer(watcher);

        foreach (var interference in new[]
        {
            "none", "two_items", "hand", "block", "death", "disconnect", "removed", "stopped"
        })
        {
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
                new VectorD(8.5, 64, 10.5));
            world.TryAddPlayer(player);
            player.GameMode = GameMode.Survival;
            var client = (TestClient)player.Client;
            player.Health = 20;
            world.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Farmland,
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
            world.BlockUpdates.SetBlock(target, BlocksRegistry.Wheat,
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
            var count = interference == "two_items" ? 2 : 1;
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.BoneMeal, count));
            var before = player.Statistics.Get("minecraft:bone_meal", "minecraft:used");
            client.ClearPackets();
            world.TakeGameEvents();

            for (var click = 0; click < 3; click++)
            {
                var packet = BlockUpdates.UseOn(target, BlockFace.Up, sequence: click + 1);
                await packet.UseAsync(context.Server, player);
            }

            Assert.Equal(before, player.Statistics.Get("minecraft:bone_meal", "minecraft:used"));
            Assert.Empty(world.TakeGameEvents());
            if (interference == "hand")
                player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Diamond, 2));
            if (interference == "block")
                world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
            if (interference == "death")
                player.Health = 0;
            if (interference == "disconnect")
                client.Dispose();
            if (interference == "removed")
                world.TryRemovePlayer(player);
            if (interference == "stopped")
                await player.StopTickingAsync();

            await world.RunQueuedEntityActionsAsync();

            var committed = interference switch
            {
                "none" => 1,
                "two_items" => 2,
                _ => 0
            };
            Assert.Equal(before + committed, player.Statistics.Get("minecraft:bone_meal", "minecraft:used"));
            Assert.Equal(committed, world.TakeGameEvents().Count(e => e.Kind == MobGameEvent.ItemInteractFinish));
            player.FlushBlockAcknowledgement();
            Assert.Equal(3, Assert.Single(client.SentPackets.OfType<BlockChangedAckPacket>()).SequenceID);
            if (interference is "none" or "two_items")
                Assert.Null(player.GetHeldItem());
            if (interference == "hand")
                Assert.Equal(2, player.GetHeldItem()?.Count);
            if (interference is "disconnect" or "removed" or "stopped")
                Assert.Equal(1, player.GetHeldItem()?.Count);

            world.TryRemovePlayer(player);
        }
    }
}
