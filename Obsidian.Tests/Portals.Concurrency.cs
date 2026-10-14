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
        var chunks = ChunkLock(player);
        var called = false;
        var unlocked = true;
        var handled = new List<IPacket>();
        source.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(async args =>
        {
            handled.Add(args.Packet);
            var free = chunks.Wait(0);
            unlocked &= free;
            if (free)
                chunks.Release();

            if (args.Packet is RespawnPacket)
            {
                called = true;
                await player.KillAsync(player, "Respawn callback");
            }
        }));

        await player.TeleportAsync(target.World, new VectorD(8, 70, 8))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(called);
        Assert.True(unlocked);
        Assert.False(player.Alive);
        Assert.Equal(0, handled.OfType<SetHealthPacket>().Last().Health);
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
