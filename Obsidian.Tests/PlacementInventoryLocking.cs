#nullable enable

using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Nbt;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.Net.Packets.Play.Clientbound;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class PlacementInventoryLocking
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
    private static readonly Vector target = new(8, 65, 8);

    [Fact]
    public async Task InventoryTickWorldWriteAndPlacementCannotDeadlockEachOther()
    {
        await using var context = TestServerFactory.CreateServer($"placement-locks-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(target);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "tick-locks.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        Assert.NotNull(await manager.LoadPluginAsync(archive));
        var player = Player(context, "User");
        var hand = player.GetHeldItem()!;
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Get("probe:charm"), 1));

        var inventoryHeld = Signal();
        var tickHeld = Signal();
        var writeRequested = Signal();
        var startWrite = Signal();
        using var startPlacement = new ManualResetEventSlim();
        using var escape = new CancellationTokenSource();
        var marker = target + Vector.East;
        Task write = Task.CompletedTask;
        Task tick = Task.CompletedTask;
        Task<bool>? placement = null;
        var hookSawUnchangedHand = false;

        AppContext.SetData(ProbePlugin.InventoryTickHookKey,
            (Func<ItemInventoryTickContext, ValueTask>)(async _ =>
            {
                inventoryHeld.TrySetResult();
                await startWrite.Task.WaitAsync(escape.Token);
                write = Task.Run(async () =>
                {
                    writeRequested.TrySetResult();
                    await world.SetBlockAsync(marker, BlocksRegistry.Dirt);
                });

                // Cancellation is only a failed-test escape: it releases the real hook's inventory lock,
                // letting a regressed placement and its pending world write unwind before server disposal.
                await write.WaitAsync(escape.Token);
                hookSawUnchangedHand = ReferenceEquals(player.GetHeldItem(), hand) && hand.Count == 2;
            }));
        try
        {
            tick = player.TickInventoryItemsAsync().AsTask();
            await inventoryHeld.Task.WaitAsync(timeout);
            placement = Task.Run(() =>
            {
                lock (world.Fluids.TickLock)
                {
                    tickHeld.TrySetResult();
                    Assert.True(startPlacement.Wait(timeout), "The inventory hook did not request its world write.");
                    return world.TrySetBlockAsync(
                        target,
                        BlocksRegistry.Stone,
                        BlockUpdateFlags.All,
                        current => current.IsAir,
                        new BlockPlacement(player, player.CurrentHeldItemSlot, hand))
                        .AsTask().GetAwaiter().GetResult();
                }
            });

            await tickHeld.Task.WaitAsync(timeout);
            startWrite.TrySetResult();
            await writeRequested.Task.WaitAsync(timeout);
            startPlacement.Set();

            try
            {
                await Task.WhenAll(tick, placement).WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Placement and inventory-tick world write did not finish: " +
                    $"placement completed={placement.IsCompleted}, item tick completed={tick.IsCompleted}. " +
                    "Tick -> inventory must never wait while the hook owns inventory -> tick.");
            }

            Assert.False(await placement);
            Assert.True(hookSawUnchangedHand);
            Assert.True(world.BlockUpdates.GetBlock(target).IsAir);
            Assert.True(world.BlockUpdates.GetBlock(marker).Is(BlockIds.Dirt));
            Assert.Same(hand, player.GetHeldItem());
            Assert.Equal(2, hand.Count);
        }
        finally
        {
            escape.Cancel();
            startWrite.TrySetResult();
            startPlacement.Set();
            await Task.WhenAll(tick, placement ?? Task.FromResult(false)).WaitAsync(timeout);
            await write.WaitAsync(timeout);
            AppContext.SetData(ProbePlugin.InventoryTickHookKey, null);
        }
    }

    [Fact]
    public async Task CompetingPlacementsConsumeExactlyOneWinningItem()
    {
        await using var context = TestServerFactory.CreateServer($"placement-race-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.SetBlockAsync(target + Vector.Down, BlocksRegistry.Dirt);
        var alice = Player(context, "Alice");
        var bob = Player(context, "Bob");
        var aliceReady = Signal();
        var bobReady = Signal();
        var releaseAlice = Signal();
        var releaseBob = Signal();
        context.EventDispatcher.RegisterEvent(null, (Func<BlockPlaceEventArgs, ValueTask>)(async args =>
        {
            var first = ReferenceEquals(args.Player, alice);
            (first ? aliceReady : bobReady).TrySetResult();
            await (first ? releaseAlice : releaseBob).Task.WaitAsync(timeout);
        }));

        var first = BlockUpdates.UseOn(target + Vector.Down, BlockFace.Up)
            .UseAsync(context.Server, alice).AsTask();
        var second = BlockUpdates.UseOn(target + Vector.Down, BlockFace.Up)
            .UseAsync(context.Server, bob).AsTask();
        try
        {
            await Task.WhenAll(aliceReady.Task, bobReady.Task).WaitAsync(timeout);
            releaseAlice.TrySetResult();
            Assert.Equal(UseResult.Success, await first.WaitAsync(timeout));
            releaseBob.TrySetResult();
            Assert.Equal(UseResult.Fail, await second.WaitAsync(timeout));

            Assert.True(context.World.BlockUpdates.GetBlock(target).Is(BlockIds.Stone));
            Assert.Equal(1, alice.GetHeldItem()?.Count);
            Assert.Equal(2, bob.GetHeldItem()?.Count);
        }
        finally
        {
            releaseAlice.TrySetResult();
            releaseBob.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(timeout);
        }
    }

    [Fact]
    public async Task BusyPlacementKeepsWorldAndHandAndRepairsPrediction()
    {
        await using var context = TestServerFactory.CreateServer($"busy-placement-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.SetBlockAsync(target + Vector.Down, BlocksRegistry.Dirt);
        var player = Player(context, "User");
        var hand = player.GetHeldItem()!;
        var client = (TestClient)player.Client;
        var releaseInventory = Signal();
        Task holder = Task.CompletedTask;
        context.EventDispatcher.RegisterEvent(null, (Func<BlockPlaceEventArgs, ValueTask>)(async _ =>
        {
            var inventoryHeld = Signal();
            holder = Task.Run(async () =>
            {
                using (await player.Inventory.LockAsync())
                {
                    inventoryHeld.TrySetResult();
                    await releaseInventory.Task.WaitAsync(timeout);
                }
            });
            await inventoryHeld.Task.WaitAsync(timeout);
        }));

        Task<UseResult?>? click = null;
        try
        {
            client.ClearPackets();
            click = Task.Run(async () => await BlockUpdates.UseOn(target + Vector.Down, BlockFace.Up, sequence: 7)
                .UseAsync(context.Server, player));
            Assert.Equal(UseResult.Fail, await click.WaitAsync(timeout));
            player.FlushBlockAcknowledgement();

            Assert.True(world.BlockUpdates.GetBlock(target).IsAir);
            Assert.Same(hand, player.GetHeldItem());
            Assert.Equal(2, hand.Count);
            var packets = client.SentPackets.ToArray();
            var ack = Array.FindIndex(packets, packet => packet is BlockChangedAckPacket);
            Assert.True(ack >= 0);
            Assert.Contains(packets.Take(ack).OfType<BlockUpdatePacket>(),
                packet => packet.Position == target && packet.BlockId == BlocksRegistry.Air.GetHashCode());
            Assert.Contains(packets.Take(ack).OfType<BlockUpdatePacket>(),
                packet => packet.Position == target + Vector.Down && packet.BlockId == BlocksRegistry.Dirt.GetHashCode());
            var slot = Assert.Single(packets.Take(ack).OfType<ContainerSetSlotPacket>());
            Assert.Equal(player.CurrentHeldItemSlot, slot.Slot);
            Assert.Equal(2, slot.SlotData?.Count);
            Assert.Equal(7, Assert.IsType<BlockChangedAckPacket>(packets.Last()).SequenceID);
        }
        finally
        {
            releaseInventory.TrySetResult();
            await holder.WaitAsync(timeout);
            if (click is not null)
                await click.WaitAsync(timeout);
        }
    }

    [Fact]
    public async Task CancelledOrInvalidatedPlacementsKeepTheirItems()
    {
        await using var context = TestServerFactory.CreateServer($"invalid-placement-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.SetBlockAsync(target + Vector.Down, BlocksRegistry.Dirt);
        var interference = "cancel";
        context.EventDispatcher.RegisterEvent(null, (Func<BlockPlaceEventArgs, ValueTask>)(args =>
        {
            switch (interference)
            {
                case "cancel":
                    args.Cancel();
                    break;
                case "leave":
                    world.TryRemovePlayer(args.Player);
                    break;
                case "hand":
                    args.Player.Inventory.SetItem(args.Player.CurrentHeldItemSlot,
                        new ItemStack(ItemsRegistry.Diamond, 3));
                    break;
            }

            return ValueTask.CompletedTask;
        }));

        foreach (var change in new[] { "cancel", "leave", "hand" })
        {
            interference = change;
            var player = Player(context, "User");
            var original = player.GetHeldItem()!;
            var click = BlockUpdates.UseOn(target + Vector.Down, BlockFace.Up);

            Assert.Equal(UseResult.Fail, await click.UseAsync(context.Server, player).AsTask().WaitAsync(timeout));

            Assert.True(world.BlockUpdates.GetBlock(target).IsAir);
            Assert.Equal(2, original.Count);
            if (change == "hand")
            {
                Assert.True(player.GetHeldItem()?.Is(ItemsRegistry.Diamond));
                Assert.Equal(3, player.GetHeldItem()?.Count);
            }
            else
            {
                Assert.Same(original, player.GetHeldItem());
            }

            world.TryRemovePlayer(player);
        }
    }

    [Fact]
    public async Task BusyCookingInventoriesSkipOrRefuseWithoutLosingItems()
    {
        await using var context = TestServerFactory.CreateServer($"cooking-locks-{Guid.NewGuid():N}");
        var world = context.World;
        world.RecipeSource = () => context.Server.RecipeRegistry.Current;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.SetBlockAsync(target, BlocksRegistry.Furnace);
        var furnace = Assert.IsType<FurnaceBlockEntity>(world.GetResidentBlockEntity(target));
        furnace.Inventory[0] = new ItemStack(ItemsRegistry.RawIron, 2);
        furnace.Inventory[1] = new ItemStack(ItemsRegistry.Coal, 1);

        await WhileLockedAsync(furnace.Inventory, () => world.TickFurnaceAsync(furnace).AsTask());
        Assert.Equal(2, furnace.Inventory[0]?.Count);
        Assert.Equal(1, furnace.Inventory[1]?.Count);
        Assert.Equal(0, furnace.CookingSpent);
        Assert.Equal(0, furnace.LitRemaining);
        await world.TickFurnaceAsync(furnace);
        Assert.True(furnace.CookingSpent > 0);

        await world.SetBlockAsync(target, BlocksRegistry.Campfire);
        var campfire = Assert.IsType<CampfireBlockEntity>(world.GetResidentBlockEntity(target));
        var player = Player(context, "Cook");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Beef, 2));
        foreach (var busy in new BaseContainer[] { player.Inventory, campfire.Inventory })
        {
            await WhileLockedAsync(busy, async () =>
            {
                Assert.False(await world.UseCampfireAsync(player, target, player.CurrentHeldItemSlot));
            });

            Assert.Equal(2, player.GetHeldItem()?.Count);
            Assert.All(campfire.Inventory, Assert.Null);
        }

        Assert.True(await world.UseCampfireAsync(player, target, player.CurrentHeldItemSlot));
        var before = BlockEntityNbt.SaveData(campfire);
        await WhileLockedAsync(campfire.Inventory, () => world.TickCampfireAsync(campfire).AsTask());
        var after = BlockEntityNbt.SaveData(campfire);
        var beforeTimes = Assert.IsType<NbtArray<int>>(before["CookingTimes"]).GetArray();
        var afterTimes = Assert.IsType<NbtArray<int>>(after["CookingTimes"]).GetArray();
        Assert.Equal(beforeTimes, afterTimes);
        Assert.Equal(1, player.GetHeldItem()?.Count);
        Assert.Equal(1, campfire.Inventory[0]?.Count);
    }

    // Hold the first lock before starting the real operation. Release and join even after a regression times out.
    private static async Task WhileLockedAsync(BaseContainer inventory, Func<Task> action)
    {
        var held = await inventory.LockAsync();
        var operation = Task.Run(action);
        try
        {
            await operation.WaitAsync(timeout);
        }
        finally
        {
            held.Dispose();
            await operation.WaitAsync(timeout);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Stone, 2));
        return player;
    }
}
