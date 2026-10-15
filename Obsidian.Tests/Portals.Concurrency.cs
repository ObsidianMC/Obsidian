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
        Func<QueuePacketEventArgs, ValueTask>? travelProbe = null;
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

            if (travelProbe is not null)
                await travelProbe(args);
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

        // Independent flows cannot overtake a suspended respawn's packet drain.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var suspend = true;
        travelProbe = async args =>
        {
            if (args.Packet is RespawnPacket && suspend)
            {
                suspend = false;
                entered.SetResult();
                await release.Task;
            }
        };
        handled.Clear();
        var respawning = player.RespawnAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var transferring = player.TeleportAsync(target.World, new VectorD(4, 70, 4)).AsTask();
        try
        {
            Assert.False(transferring.IsCompleted);
            Assert.Same(source.World, player.Level);
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(respawning, transferring).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Same(target.World, player.Level);
        Assert.Equal(new VectorD(4, 70, 4), player.Position);
        Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        Assert.Equal(2, handled.OfType<RespawnPacket>().Count());

        // A same-level teleport awaited by the respawn handler follows the saved respawn position.
        var teleport = true;
        var sameLevelDeferred = false;
        travelProbe = async args =>
        {
            if (args.Packet is RespawnPacket && teleport)
            {
                teleport = false;
                var before = player.Position;
                await player.TeleportAsync(player.Level, new VectorD(8, 70, 8));
                sameLevelDeferred = before == player.Position;
            }
        };
        handled.Clear();
        await player.RespawnAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(sameLevelDeferred);
        Assert.Equal(new VectorD(8, 70, 8), player.Position);
        Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        Assert.Single(handled.OfType<RespawnPacket>());

        // Later destination handlers and event callbacks belong to the same sequence as the initial drain.
        source.World.LevelData.Time = 12345;
        target.World.LevelData.Time = 67890;
        var returnToTarget = true;
        var destinationDeferred = false;
        travelProbe = async args =>
        {
            if (args.Packet is SetTimePacket && returnToTarget)
            {
                returnToTarget = false;
                await player.TeleportAsync(target.World, new VectorD(4, 70, 4));
                destinationDeferred = ReferenceEquals(source.World, player.Level);
            }
        };
        handled.Clear();
        await player.TeleportAsync(source.World, new VectorD(6, 70, 6))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(destinationDeferred);
        Assert.Same(target.World, player.Level);
        Assert.Equal(player.Position, handled.OfType<PlayerPositionPacket>().Last().Position);
        Assert.Equal(new long[] { 12345, 67890 }, handled.OfType<SetTimePacket>().Select(packet => packet.WorldAge));
        Assert.True(unlocked, string.Join(", ", lockedPackets));

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
    }

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
