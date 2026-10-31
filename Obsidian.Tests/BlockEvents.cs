using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Registries;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData.BlockNotifications;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BlockEvents
{
    [Fact]
    public async Task DrainAllowsTickLockedEnqueueFromAnotherThread()
    {
        await using var context = TestServerFactory.CreateServer($"block-events-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener");
        world.Players.TryAdd(listener.Uuid, listener);
        var position = new Vector(0, 72, 0);
        world.BlockUpdates.SetBlock(position, BlocksRegistry.Chest,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var completed = false;
        world.QueueBlockEvent(position, BlockIds.Chest, 1, 0, async () =>
        {
            // A block callback can await work that takes the tick lock and queues another event.
            // Holding either lock across the callback would deadlock this worker.
            await Task.Run(() =>
            {
                lock (world.Fluids.TickLock)
                {
                    world.QueueBlockEvent(position, BlockIds.Chest, 1, 0, () =>
                    {
                        completed = true;
                        return ValueTask.FromResult(true);
                    });
                }
            }).WaitAsync(TimeSpan.FromSeconds(10));

            return true;
        });

        await world.ProcessBlockEventsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(completed);
        Assert.Equal(0, world.PendingBlockEvents);
    }

    [Fact]
    public async Task NoteEventPrecedesPistonBlockUpdates()
    {
        await using var context = TestServerFactory.CreateServer($"block-event-order-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener");
        world.Players.TryAdd(listener.Uuid, listener);
        listener.LoadedChunks.Add(NumericsHelper.IntsToLong(-1, 0));
        listener.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        var note = new Vector(0, 72, 0);
        var piston = new Vector(-1, 72, 0);
        var power = new Vector(-2, 72, 0);
        world.BlockUpdates.SetBlock(note, BlocksRegistry.NoteBlock,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        world.BlockUpdates.SetBlock(piston, BlocksRegistry.Piston.WithProperty("facing", "east"),
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        world.BlockUpdates.SetBlock(power, BlocksRegistry.RedstoneBlock,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var client = Assert.IsType<TestClient>(listener.Client);
        client.ClearPackets();
        context.Broadcaster.DeferRangePackets = true;

        world.PlayNoteBlock(note);
        world.BlockNotifications.EnqueueNeighbor(piston, power);
        await world.BlockNotifications.DrainAsync();
        await world.DoWorldTickAsync();
        context.Broadcaster.FlushRangePackets();

        var packets = client.SentPackets.ToList();
        var noteEvent = packets.FindIndex(packet => packet is BlockEventPacket blockEvent
            && blockEvent.BlockType == BlockIds.NoteBlock && blockEvent.Position == note);
        var pistonWrite = packets.FindIndex(packet => packet is BlockUpdatePacket update && update.Position == note);
        Assert.True(noteEvent >= 0, "The client did not receive the note event.");
        Assert.True(pistonWrite >= 0, "The client did not receive the piston block update.");
        Assert.True(noteEvent < pistonWrite, "The piston block update overtook the note event.");

        var noteSound = packets.FindIndex(packet => packet is SoundPacket sound
            && sound.SoundLocation == "minecraft:block.note_block.harp");
        var pistonSound = packets.FindIndex(packet => packet is SoundPacket sound
            && sound.SoundLocation == "minecraft:block.piston.extend");
        Assert.True(noteSound >= 0 && pistonSound >= 0, "The client did not receive both block sounds.");
        Assert.True(noteSound < noteEvent && noteSound < pistonSound,
            "The note sound was reordered behind its event or the piston sound.");
    }

    [Fact]
    public async Task ContainerLidsReportPhysicalViewerCounts()
    {
        var cases = new[]
        {
            (Block: BlocksRegistry.Chest, Double: false),
            (Block: BlocksRegistry.TrappedChest, Double: false),
            (Block: BlocksRegistry.GetDefault(BlockIds.CopperChest), Double: false),
            (Block: BlocksRegistry.EnderChest, Double: false),
            (Block: BlocksRegistry.ShulkerBox, Double: false),
            (Block: BlocksRegistry.GetDefault(BlockIds.RedShulkerBox), Double: false),
            (Block: BlocksRegistry.Chest, Double: true),
            (Block: BlocksRegistry.TrappedChest, Double: true),
            (Block: BlocksRegistry.GetDefault(BlockIds.CopperChest), Double: true)
        };
        await FixtureCases.AssertAllAsync(cases, item => $"{item.Block.UnlocalizedName}/double={item.Double}",
            async item =>
            {
                await using var context = TestServerFactory.CreateServer($"lid-counts-{Guid.NewGuid():N}");
                var world = context.World;
                await BlockUpdates.AddChunksAsync(world);
                var alice = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Alice");
                var bob = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bob");
                world.Players.TryAdd(alice.Uuid, alice);
                world.Players.TryAdd(bob.Uuid, bob);
                var left = new Vector(0, 72, 0);
                var right = new Vector(1, 72, 0);
                var first = item.Double
                    ? item.Block.WithProperty("facing", "north").WithProperty("type", "left") : item.Block;
                var second = item.Double
                    ? item.Block.WithProperty("facing", "north").WithProperty("type", "right") : item.Block;
                world.BlockUpdates.SetBlock(left, first,
                    BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
                if (item.Double)
                {
                    world.BlockUpdates.SetBlock(right, second,
                        BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
                }

                await Open(alice, first, left);
                await Check(1);
                await Open(bob, item.Double ? second : first, item.Double ? right : left);
                await Check(2);
                if (item.Block.RegistryId == BlockIds.EnderChest)
                {
                    Assert.NotSame(alice.OpenedContainer, bob.OpenedContainer);
                    var other = new Vector(3, 72, 0);
                    world.BlockUpdates.SetBlock(other, item.Block,
                        BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
                    var charlie = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Charlie");
                    world.Players.TryAdd(charlie.Uuid, charlie);
                    await Open(charlie, item.Block, other);
                    await world.ProcessBlockEventsAsync();
                    foreach (var viewer in new[] { alice, bob, charlie })
                    {
                        var client = Assert.IsType<TestClient>(viewer.Client);
                        var packet = Assert.Single(client.SentPackets.OfType<BlockEventPacket>());
                        Assert.Equal(other, packet.Position);
                        Assert.Equal(1, packet.ActionParam);
                        client.ClearPackets();
                    }
                }

                await Close(alice);
                await Check(1);
                Assert.NotNull(bob.OpenedContainer);
                await Close(bob);
                await Check(0);

                async Task Open(Obsidian.Entities.Player player, Obsidian.API.IBlock block, Vector position)
                {
                    await context.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(player, context.Server)
                    {
                        Block = block,
                        BlockLocation = position
                    });
                }

                async Task Close(Obsidian.Entities.Player player)
                {
                    await context.EventDispatcher.ExecuteEventAsync(new ContainerClosedEventArgs(
                        player, context.Server, player.OpenedContainer!, player.CurrentContainerId));
                }

                async Task Check(int count)
                {
                    await world.ProcessBlockEventsAsync();
                    foreach (var viewer in new[] { alice, bob })
                    {
                        var client = Assert.IsType<TestClient>(viewer.Client);
                        var events = client.SentPackets.OfType<BlockEventPacket>().ToArray();
                        Assert.Equal(item.Double ? 2 : 1, events.Length);
                        Assert.All(events, packet =>
                        {
                            Assert.Equal(item.Block.RegistryId, packet.BlockType);
                            Assert.Equal(1, packet.ActionId);
                            Assert.Equal(count, packet.ActionParam);
                        });
                        Assert.Contains(events, packet => packet.Position == left);
                        if (item.Double)
                        {
                            Assert.Contains(events, packet => packet.Position == right);
                        }

                        client.ClearPackets();
                    }
                }
            });
    }

}
