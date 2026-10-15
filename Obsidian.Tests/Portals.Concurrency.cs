using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Data;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public partial class Portals
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DimensionTransfersWaitForDeathsAndReleaseLocksBeforeCallbacks()
    {
        await AssertTransferWaitsForDeathBeforeCommittingAsync();
        await AssertRespawnCallbackCanKillDuringDimensionTransferAsync();
    }

    private static async Task AssertRespawnCallbackCanKillDuringDimensionTransferAsync()
    {
        await using var source = TestServerFactory.CreateServer($"callback-source-{Guid.NewGuid():N}");
        await using var target = TestServerFactory.CreateServer($"callback-target-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(source.World);
        WorldBlockEntities.AddEmptyChunk(target.World);
        WorldBlockEntities.AddEmptyChunk(target.World, 1);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await peer.ConnectAsync(listener.LocalEndpoint);
        using var socket = await listener.AcceptSocketAsync();
        using var client = new Client(
            source.EventDispatcher, source.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), null!, source.Provider, null!);
        typeof(Client).GetProperty(nameof(Client.Socket))!.SetValue(client, socket);
        client.SetState(ClientState.Play);
        var player = new Entities.Player(Guid.NewGuid(), "Callback", client, source.World)
        {
            Server = source.Server,
            Position = source.World.LevelData.SpawnPosition
        };
        Assert.True(source.World.TryAddPlayer(player));
        Assert.True(source.World.TryAddEntity(player));
        player.OnChunkBatchReceived(64);
        using var observer = new Client(
            source.EventDispatcher, source.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), null!, source.Provider, null!);
        typeof(Client).GetProperty(nameof(Client.Socket))!.SetValue(observer, peer);
        observer.SetState(ClientState.Play);
        var bystander = TestServerFactory.CreateConnectedPlayer(source.Server, source.World, "Bystander");
        Assert.True(source.World.TryAddPlayer(bystander));
        Assert.True(source.World.TryAddEntity(bystander));

        // What the handlers saw, in each client's pipeline order. They cancel every packet: these clients have no
        // socket loops to send through.
        var handled = new List<IPacket>();
        var observed = new List<IPacket>();
        Func<QueuePacketEventArgs, ValueTask> probe = null;
        source.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(async args =>
        {
            args.Cancel();
            if (ReferenceEquals(args.Client, observer))
            {
                observed.Add(args.Packet);
                return;
            }

            handled.Add(args.Packet);
            if (probe is not null)
                await probe(args);
        }));

        // Queueing never waits for handlers. One that hasn't returned holds up only its own client's later packets,
        // a direct send waits behind them, and a send that fails (there's no socket loop) doesn't stop the pipeline.
        var first = new SetHealthPacket(20, 20, 5);
        var second = new SetExperiencePacket(0, 0, 0);
        var third = new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks);
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe = async args =>
        {
            if (ReferenceEquals(args.Packet, first))
            {
                stalled.SetResult();
                await resume.Task;
            }
        };
        Assert.True(client.QueuePacketAsync(first).IsCompleted);
        await stalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(client.SendPacket(second));
        await client.QueuePacketAsync(third);
        Assert.Equal(3, Unsent(client));
        await observer.QueuePacketAsync(first);
        await observer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(first, Assert.Single(observed));
        resume.SetResult();
        await client.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal<IPacket>([first, third], handled);
        Assert.Equal(0, Unsent(client));

        // A respawn packet's handler that waits for the chunk lock and kills the player doesn't hold up the level
        // change that queued it, and nothing of the new level reaches the client before its respawn and position.
        handled.Clear();
        probe = async args =>
        {
            if (args.Packet is not RespawnPacket)
                return;

            await player.SendPendingChunksAsync();
            await player.UpdateChunksAsync(distance: 2);
            await player.KillAsync(player, "Respawn callback");
        };
        await player.TeleportAsync(target.World, new VectorD(8, 70, 8))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        await client.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(player.Alive);
        Assert.Equal(0, handled.OfType<SetHealthPacket>().Last().Health);
        Assert.NotEmpty(handled.OfType<LevelChunkWithLightPacket>());
        var respawnIndex = handled.FindIndex(packet => packet is RespawnPacket);
        var positionIndex = handled.FindIndex(packet => packet is PlayerPositionPacket);
        Assert.True(respawnIndex >= 0 && positionIndex > respawnIndex);
        Assert.All(handled.OfType<LevelChunkWithLightPacket>(), packet =>
            Assert.True(handled.IndexOf(packet) > positionIndex));

        // A handler can wait for another player's death and respawn, and for this player's respawn and level change.
        // A plugin's attachment callback that asks for a respawn from inside the respawn's own callbacks gets it once
        // they're done, without waiting for itself.
        var owner = new PluginContainer
        {
            Info = new PluginInfo("travel"),
            Archive = PluginArchive.Open("travel.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("travel", "1.0.0", "Travel")
            }))
        };
        try
        {
            var mark = PluginRegistry.RegisterAttachment(owner, "travel:mark", new ComponentCodec<int>(
                value => new NbtTag<int>("", value),
                tag => ((NbtTag<int>)tag).Value), null, copyOnDeath: false);
            player.SetAttachment(mark, 1);
            mark.Changed += holder =>
            {
                if (ReferenceEquals(holder, player))
                    _ = player.RespawnAsync();
            };
            var reenter = true;
            handled.Clear();
            probe = async args =>
            {
                if (args.Packet is not RespawnPacket || !reenter)
                    return;

                reenter = false;
                await bystander.KillAsync(bystander, "Bystander");
                await bystander.RespawnAsync();
                await player.RespawnAsync();
                await player.TeleportAsync(source.World, new VectorD(8, 70, 8));
            };
            await player.RespawnAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await client.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(player.Alive);
            Assert.True(bystander.Alive);
            Assert.False(player.TryGetAttachment(mark, out _));
            Assert.Same(source.World, player.Level);
            Assert.Equal(4, handled.OfType<RespawnPacket>().Count());
            Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        }
        finally
        {
            owner.Close();
            owner.UndoHostAdditions();
            await owner.DisposeAsync();
        }

        // A handler can wait for a flow that shares nothing with it (no deferral can reach it) moving the same player
        // to another level while the respawn that queued the packet runs.
        Task worker = null;
        probe = async args =>
        {
            if (args.Packet is not RespawnPacket || worker is not null)
                return;

            using (ExecutionContext.SuppressFlow())
                worker = Task.Run(() => player.TeleportAsync(target.World, new VectorD(4, 70, 4)).AsTask());

            await worker;
        };
        handled.Clear();
        await player.RespawnAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await client.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(target.World, player.Level);
        Assert.Equal(new VectorD(4, 70, 4), player.Position);
        Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        Assert.Equal(2, handled.OfType<RespawnPacket>().Count());

        // A same-level teleport awaited by the respawn handler completes there, after the respawn.
        var teleport = true;
        var teleported = false;
        probe = async args =>
        {
            if (args.Packet is RespawnPacket && teleport)
            {
                teleport = false;
                await player.TeleportAsync(player.Level, new VectorD(8, 70, 8));
                teleported = player.Position == new VectorD(8, 70, 8);
            }
        };
        handled.Clear();
        await player.RespawnAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await client.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(teleported);
        Assert.Equal(new VectorD(8, 70, 8), player.Position);
        Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        Assert.Single(handled.OfType<RespawnPacket>());

        // A level change awaited by a handler of the previous one's destination packets follows all of them.
        source.World.LevelData.Time = 12345;
        target.World.LevelData.Time = 67890;
        var returnToTarget = true;
        var returned = false;
        probe = async args =>
        {
            if (args.Packet is SetTimePacket && returnToTarget)
            {
                returnToTarget = false;
                await player.TeleportAsync(target.World, new VectorD(4, 70, 4));
                returned = ReferenceEquals(target.World, player.Level);
            }
        };
        handled.Clear();
        await player.TeleportAsync(source.World, new VectorD(6, 70, 6))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        await client.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(returned);
        Assert.Same(target.World, player.Level);
        Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        Assert.Equal(new long[] { 12345, 67890 }, handled.OfType<SetTimePacket>().Select(packet => packet.WorldAge));
        var lastRespawn = handled.FindLastIndex(packet => packet is RespawnPacket);
        Assert.Equal(67890, handled.Skip(lastRespawn).OfType<SetTimePacket>().Single().WorldAge);

        // The fake broadcaster uses direct sends for mounts. Switch to its recording client for the passenger
        // checks: a vehicle must await a player passenger's actual transfer before seating it again.
        var recording = new TestClient(source.Server);
        recording.Attach(player);
        player.Client = recording;
        var rider = TestServerFactory.CreateConnectedPlayer(
            target.Server, target.World, "SequenceRider", player.Position + VectorD.Up);
        Assert.True(target.World.TryAddPlayer(rider));
        Assert.True(target.World.TryAddEntity(rider));
        Assert.True(rider.Ride(player));
        await player.TeleportAsync(source.World, new VectorD(6, 70, 6));
        Assert.Same(source.World, rider.Level);
        Assert.Equal(player.Position + VectorD.Up, rider.Position);
        Assert.Same(player, rider.Vehicle);
        await player.TeleportAsync(source.World, new VectorD(8, 70, 8));
        Assert.Equal(player.Position + VectorD.Up, rider.Position);
        Assert.Same(player, rider.Vehicle);
    }

    private static async Task AssertTransferWaitsForDeathBeforeCommittingAsync()
    {
        await using var source = TestServerFactory.CreateServer($"death-source-{Guid.NewGuid():N}");
        await using var target = TestServerFactory.CreateServer($"death-target-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(source.World);
        WorldBlockEntities.AddEmptyChunk(target.World);
        var player = TestServerFactory.CreateConnectedPlayer(source.Server, source.World, "Dying");
        Assert.True(source.World.TryAddPlayer(player));
        Assert.True(source.World.TryAddEntity(player));
        var chunks = ChunkLock(player);
        await chunks.WaitAsync();
        var moving = player.TeleportAsync(target.World, new VectorD(8, 70, 8)).AsTask();
        Assert.False(moving.IsCompleted);
        Task dying;
        using (await player.Inventory.LockAsync())
        {
            dying = player.KillAsync(player, "Concurrent death").AsTask();
            Assert.False(dying.IsCompleted);
            chunks.Release();
            // A second waiter establishes that the transfer has passed through chunkUpdates.
            await chunks.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            chunks.Release();
            Assert.Same(source.World, player.Level);
            Assert.False(moving.IsCompleted);
        }

        await Task.WhenAll(dying, moving).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(target.World, player.Level);
        Assert.True(player.Alive);
        var packets = ((TestClient)player.Client).SentPackets;
        Assert.Equal(DataKept.None, Assert.Single(packets.OfType<RespawnPacket>()).DataKept);
        Assert.Single(packets.OfType<PlayerCombatKillPacket>());

        // Closing an inner transaction must hand its death to the outer queue, even if the outer body throws.
        // Work scheduled by a handed-over item and work after a failing item must each run exactly once.
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Diamond));
        var order = new List<int>();
        var failure = new InvalidOperationException("Outer transaction failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var outer = DeferredInventoryWork.Begin(NullLogger.Instance);
            using (await player.Inventory.LockAsync())
            {
                await DeferredInventoryWork.RunOrDeferAsync(() => Record(1));
                var inner = DeferredInventoryWork.Begin(NullLogger.Instance);
                try
                {
                    await player.KillAsync(player, "Nested inventory death");
                    await DeferredInventoryWork.RunOrDeferAsync(async () =>
                    {
                        await using var followup = DeferredInventoryWork.Begin(NullLogger.Instance);
                        using (await player.Inventory.LockAsync())
                        {
                            order.Add(2);
                            await DeferredInventoryWork.RunOrDeferAsync(() => Record(3));
                            Assert.Equal<int>([1, 2], order);
                        }
                    });
                    await DeferredInventoryWork.RunOrDeferAsync(
                        () => throw new InvalidOperationException("Deferred item failed"));
                }
                finally
                {
                    await inner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                }

                Assert.Equal(1, player.Inventory.GetItem(9)?.Count);
                Assert.Empty(order);
                await DeferredInventoryWork.RunOrDeferAsync(() => Record(4));
                throw failure;
            }
        });
        Assert.Same(failure, thrown);
        Assert.Equal<int>([1, 2, 3, 4], order);
        Assert.All(player.Inventory, Assert.Null);
        Assert.Equal(2, packets.OfType<PlayerCombatKillPacket>().Count());
        Assert.Single(target.World.GetNonPlayerEntitiesInRange(player.Position, 8).OfType<ItemEntity>(),
            entity => entity.Item.Is(ItemsRegistry.Diamond));

        ValueTask Record(int value)
        {
            order.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentMountsCannotCreateRidingCycles()
    {
        await using var context = TestServerFactory.CreateServer($"riding-race-{Guid.NewGuid():N}");
        using var gate = new Barrier(2);
        var first = new GateCow(gate)
        {
            Level = context.World
        };
        var second = new GateCow(gate)
        {
            Level = context.World
        };
        var results = await Task.WhenAll(
            Task.Run(() => first.StartRiding(second)),
            Task.Run(() => second.StartRiding(first)));
        Assert.NotEqual(results[0], results[1]);
        Assert.Same(first.RootVehicle, second.RootVehicle);
        Assert.Single(first.Passengers.Concat(second.Passengers));

        // Reads and mutation requests from a click must finish while pickup owns TransferLock. The queued mounts
        // still check cycles together when delivered, and earlier snapshots keep their original passengers.
        var vehicle = new Cow { Level = context.World };
        var rider = new Cow { Level = context.World };
        var replacement = new Cow { Level = context.World };
        Assert.True(rider.StartRiding(vehicle));
        var snapshot = ((IEntity)vehicle).Passengers;
        using var release = new ManualResetEventSlim();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = Task.Run(() =>
        {
            lock (ItemEntity.TransferLock)
            {
                held.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
        });
        await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (DeferredInventoryWork.Begin(NullLogger.Instance))
        {
            try
            {
                var container = new Container(5);
                using (await container.LockAsync())
                {
                    await Task.Run(() =>
                    {
                        Assert.Same(rider, Assert.Single(((IEntity)vehicle).Passengers));
                        Assert.Same(vehicle, rider.RootVehicle);
                        Assert.Same(vehicle, ((IEntity)rider).Vehicle);
                        rider.StopRiding();
                        Assert.True(vehicle.StartRiding(replacement));
                        Assert.True(replacement.StartRiding(vehicle));
                        Assert.Same(vehicle, rider.Vehicle);
                        Assert.Null(vehicle.Vehicle);
                    }).WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            finally
            {
                release.Set();
                await holding.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        Assert.Null(rider.Vehicle);
        Assert.Same(replacement, vehicle.Vehicle);
        Assert.Null(replacement.Vehicle);
        Assert.Empty(vehicle.Passengers);
        Assert.Same(vehicle, Assert.Single(replacement.Passengers));
        Assert.Same(rider, Assert.Single(snapshot));
    }

    // Entries in the client's pipeline not yet sent or dropped.
    private static int Unsent(Client client) =>
        (int)typeof(Client).GetField("unsent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;

    private static SemaphoreSlim ChunkLock(Entities.Player player) =>
        (SemaphoreSlim)typeof(Entities.Player)
            .GetField("chunkUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(player)!;

    private sealed class GateCow(Barrier gate) : Cow
    {
        protected override int MaximumPassengers
        {
            get
            {
                Assert.False(Monitor.IsEntered(ItemEntity.TransferLock));
                Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
                return 1;
            }
        }
    }
}
