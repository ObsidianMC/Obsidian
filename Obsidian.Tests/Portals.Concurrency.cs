using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
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
        var outgoing = (Channel<IClientboundPacket>)typeof(Client)
            .GetField("packetQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        var first = new SetHealthPacket(20, 20, 5);
        var second = new SetExperiencePacket(0, 0, 0);
        var third = new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks);
        using var observer = new Client(
            source.EventDispatcher, source.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), null!, source.Provider, null!);
        typeof(Client).GetProperty(nameof(Client.Socket))!.SetValue(observer, peer);
        observer.SetState(ClientState.Play);
        var observerOutgoing = (Channel<IClientboundPacket>)typeof(Client)
            .GetField("packetQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(observer)!;
        await using (DeferredInventoryWork.Begin(player.Logger, packetClient: client))
        {
            await client.QueuePacketAsync(first);
            Assert.True(client.SendPacket(second));
            client.QueueWithoutHandlers(third);
            await observer.QueuePacketAsync(first);
            await observer.QueuePacketAsync(second);
            Assert.True(observerOutgoing.Reader.TryRead(out var observedFirst));
            Assert.True(observerOutgoing.Reader.TryRead(out var observedSecond));
            Assert.Same(first, observedFirst);
            Assert.Same(second, observedSecond);
            Assert.False(outgoing.Reader.TryPeek(out _));
        }

        Assert.True(outgoing.Reader.TryRead(out var queuedFirst));
        Assert.True(outgoing.Reader.TryRead(out var queuedSecond));
        Assert.True(outgoing.Reader.TryRead(out var queuedThird));
        Assert.Same(first, queuedFirst);
        Assert.Same(second, queuedSecond);
        Assert.Same(third, queuedThird);

        var chunks = ChunkLock(player);
        var called = false;
        var kill = true;
        var reenter = false;
        var unlocked = true;
        var chunkUpdatesBlocked = false;
        var prematureChunks = -1;
        var handled = new List<IPacket>();
        var lockedPackets = new List<string>();
        source.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(async args =>
        {
            handled.Add(args.Packet);
            var free = chunks.Wait(0);
            unlocked &= free;
            if (free)
                chunks.Release();
            else
                lockedPackets.Add(args.Packet.GetType().Name);

            if (args.Packet is RespawnPacket)
            {
                called = true;
                await player.SendPendingChunksAsync();
                chunkUpdatesBlocked = !await player.UpdateChunksAsync(distance: 2);
                prematureChunks = handled.OfType<LevelChunkWithLightPacket>().Count();
                if (kill)
                    await player.KillAsync(player, "Respawn callback");
                else if (reenter)
                {
                    reenter = false;
                    await player.RespawnAsync();
                    await player.TeleportAsync(source.World, new VectorD(8, 70, 8));
                }
            }
        }));

        await player.TeleportAsync(target.World, new VectorD(8, 70, 8))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(called);
        Assert.True(unlocked, string.Join(", ", lockedPackets));
        Assert.True(chunkUpdatesBlocked);
        Assert.Equal(0, prematureChunks);
        Assert.False(player.Alive);
        Assert.Equal(0, handled.OfType<SetHealthPacket>().Last().Health);
        await player.SendPendingChunksAsync();
        Assert.NotEmpty(handled.OfType<LevelChunkWithLightPacket>());
        var queued = new List<IClientboundPacket>();
        while (outgoing.Reader.TryRead(out var packet))
            queued.Add(packet);

        var respawnIndex = queued.FindIndex(packet => packet is RespawnPacket);
        var positionIndex = queued.FindIndex(packet => packet is PlayerPositionPacket);
        Assert.True(respawnIndex >= 0 && positionIndex > respawnIndex);
        Assert.All(queued.OfType<LevelChunkWithLightPacket>(), packet =>
            Assert.True(queued.IndexOf(packet) > positionIndex));

        kill = false;
        reenter = true;
        unlocked = true;
        lockedPackets.Clear();
        handled.Clear();
        await player.RespawnAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(player.Alive);
        Assert.True(unlocked, string.Join(", ", lockedPackets));
        Assert.Same(source.World, player.Level);
        Assert.Equal(3, handled.OfType<RespawnPacket>().Count());
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

    private static SemaphoreSlim ChunkLock(Entities.Player player) =>
        (SemaphoreSlim)typeof(Entities.Player).GetField("chunkUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!
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
