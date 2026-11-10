using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Configuration.Serverbound;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MoveVehiclePacket = Obsidian.Net.Packets.Play.Serverbound.MoveVehiclePacket;

namespace Obsidian.Tests;

// Protocol regressions: real players, parsed serverbound packets, and packets delivered to watching clients.
public sealed class PendingTeleports
{
    private static readonly VectorD start = new(4.5, 80, 4.5);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task MovementWaitsForConfirmationButRotationStillWorks()
    {
        await using var context = Create();
        var player = AddPlayer(context, "Mover");
        var watcher = AddPlayer(context, "Watcher");
        var client = (TestClient)player.Client;
        var watching = (TestClient)watcher.Client;
        player.MovementFlags = MovementFlags.OnGround;
        var destination = start + new VectorD(0, 0.125, 0);
        await player.TeleportAsync(destination);
        watching.ClearPackets();

        ServerboundPacket[] stale =
        [
            Move(start),
            MoveWithRotation(start, 45, 0),
            Rotate(90, 45),
            Status(MovementFlags.HorizontalCollision)
        ];
        foreach (var packet in stale)
        {
            await packet.HandleAsync(context.Server, player);
            Assert.Equal(destination, player.Position);
            Assert.Equal(MovementFlags.OnGround, player.MovementFlags);
            Assert.DoesNotContain(watching.SentPackets, IsPositionUpdate);
        }

        Assert.Equal(((Angle)90f, (Angle)45f), (player.Yaw, player.Pitch));
        Assert.Contains(watching.SentPackets, packet => packet is MoveEntityRotPacket { Yaw: var yaw }
            && yaw == (Angle)90f);

        var support = new Vector(6, 79, 4);
        await context.World.SetBlockAsync(support, BlocksRegistry.Stone);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Stone));
        await BlockUpdates.UseOn(support, BlockFace.Up).HandleAsync(context.Server, player);
        Assert.True((await context.World.GetBlockAsync(support + Vector.Up))!.IsAir);
        Assert.Contains(client.SentPackets, packet => packet is BlockChangedAckPacket);

        // The 26.3 confirmation's reported position is applied from the server destination, with cleared flags.
        var resolved = destination + new VectorD(0.25, 0, 0);
        await client.ConfirmTeleportAsync(position: resolved);
        Assert.Equal(resolved, player.Position);
        Assert.Equal(MovementFlags.None, player.MovementFlags);
        var confirmationMove = Assert.Single(watching.SentPackets.OfType<MoveEntityPosRotPacket>());
        Assert.Equal(new Vector(1024, 0, 0), confirmationMove.Delta);
        watching.ClearPackets();

        var next = resolved + new VectorD(0.5, 0, 0);
        await Move(next).HandleAsync(context.Server, player);

        Assert.Equal(next, player.Position);
        Assert.Equal(new Vector(2048, 0, 0), Assert.Single(watching.SentPackets.OfType<MoveEntityPosPacket>()).Delta);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task NewTeleportRejectsOldConfirmationsIncludingAcrossIdWrap()
    {
        await using var context = Create();
        (int Counter, int First, int Second)[] cases = [(0, 1, 2), (int.MaxValue - 2, int.MaxValue - 1, 0)];
        foreach (var row in cases)
        {
            var player = AddPlayer(context, $"Replacement-{row.Counter}");
            var client = (TestClient)player.Client;
            // The public compatibility setter also supports plugins that have already issued an id.
            player.TeleportId = row.Counter;
            await player.TeleportAsync(start + new VectorD(1, 0, 0));
            var first = client.SentPackets.OfType<PlayerPositionPacket>().Last();
            await player.TeleportAsync(start + new VectorD(2, 0, 0));
            var second = client.SentPackets.OfType<PlayerPositionPacket>().Last();
            Assert.Equal((row.First, row.Second), (first.TeleportId, second.TeleportId));

            await client.ConfirmTeleportAsync(first);
            await Move(start).HandleAsync(context.Server, player);
            Assert.Equal(second.Position, player.Position);
            Assert.True(client.Connected);

            await client.ConfirmTeleportAsync(second);
            var next = second.Position + new VectorD(0.25, 0, 0);
            await Move(next).HandleAsync(context.Server, player);
            Assert.Equal(next, player.Position);

            // A duplicate of the current id with nothing pending is invalid in vanilla, rather than another move.
            await client.ConfirmTeleportAsync(second);
            Assert.False(client.Connected);
            Assert.Equal(next, player.Position);
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task MissingConfirmationRetriesOnMovementAfterTwentyTicks()
    {
        await using var context = Create();
        var player = AddPlayer(context, "Retry");
        var client = (TestClient)player.Client;
        player.Motion = new VectorD(0.2, 0.3, 0.4);
        await player.TeleportAsync(start + new VectorD(1, 0, 0));
        var original = client.SentPackets.OfType<PlayerPositionPacket>().Last();
        await Rotate(90, 45).HandleAsync(context.Server, player);

        for (var tick = 0; tick < 20; tick++)
            await player.TickAsync();
        await Status(MovementFlags.OnGround).HandleAsync(context.Server, player);
        Assert.Single(client.SentPackets.OfType<PlayerPositionPacket>());

        await player.TickAsync();
        // The clock alone doesn't resend. A vehicle packet triggers it even if the player has no vehicle.
        Assert.Single(client.SentPackets.OfType<PlayerPositionPacket>());
        await VehicleMove(start).HandleAsync(context.Server, player);
        var retry = client.SentPackets.OfType<PlayerPositionPacket>().Last();
        Assert.Equal(original.TeleportId + 1, retry.TeleportId);
        Assert.Equal(original.Position, retry.Position);
        Assert.Equal(VectorD.Zero, retry.Delta);
        Assert.Equal(VectorD.Zero, player.Motion);
        Assert.Equal(0, retry.RelativeFlags);
        Assert.Equal(((Angle)90f, (Angle)45f), (retry.Yaw, retry.Pitch));

        await client.ConfirmTeleportAsync(original);
        await Move(start).HandleAsync(context.Server, player);
        Assert.Equal(retry.Position, player.Position);
        await client.ConfirmTeleportAsync(retry);
        await Move(start).HandleAsync(context.Server, player);
        Assert.Equal(start, player.Position);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConfirmingClientCanMoveFirstAfterLoginAndRespawn()
    {
        await using var context = Create();
        context.World.LevelData.SpawnPosition = start;
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Joining");
        var client = (TestClient)player.Client;
        Assert.True(context.World.TryAddPlayer(player));
        Assert.True(context.World.TryAddEntity(player));

        await new FinishConfigurationPacket().HandleAsync(context.Server, player);
        var login = client.SentPackets.OfType<PlayerPositionPacket>().Last();
        await client.ConfirmTeleportAsync(login);
        var first = login.Position + new VectorD(0.5, 0, 0);
        await MoveWithRotation(first, 45, 0).HandleAsync(context.Server, player);
        Assert.Equal(first, player.Position);

        await player.KillAsync(player, "Test respawn");
        await player.RespawnAsync();
        var respawn = client.SentPackets.OfType<PlayerPositionPacket>().Last();
        Assert.NotEqual(login.TeleportId, respawn.TeleportId);
        await Move(first).HandleAsync(context.Server, player);
        Assert.Equal(respawn.Position, player.Position);

        await client.ConfirmTeleportAsync(respawn);
        var next = respawn.Position + new VectorD(0.75, 0, 0);
        await MoveWithRotation(next, 90, 0).HandleAsync(context.Server, player);
        Assert.Equal(next, player.Position);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task TeleportWinsWhileAnEarlierMovementAwaitsItsChunk()
    {
        await using var context = Create();
        var world = context.World;
        var player = AddPlayer(context, "Concurrent");
        var watcher = AddPlayer(context, "ConcurrentWatcher");
        var watching = (TestClient)watcher.Client;
        var region = (Region)world.LoadRegionByChunk(1, 0);
        region.SetChunk(new Chunk(1, 0, world.MinY, world.Height, ChunkGenStage.full));
        await region.UnloadChunk(1, 0);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.ChunkFile.Observe = step =>
        {
            if (step is not { Step: RegionFile.Step.Reading, ChunkX: 1, ChunkZ: 0 })
                return default;

            reading.TrySetResult();
            return new ValueTask(release.Task);
        };

        var movement = Move(new VectorD(16.5, 80, 4.5)).HandleAsync(context.Server, player).AsTask();
        try
        {
            await reading.Task;
            await player.TeleportAsync(start + new VectorD(0, 0.125, 0));
            // Confirm before releasing the old movement as well: checking only "pending" is insufficient here.
            await ((TestClient)player.Client).ConfirmTeleportAsync();
            watching.ClearPackets();
        }
        finally
        {
            release.TrySetResult();
        }

        await movement;
        region.ChunkFile.Observe = null;
        Assert.Equal(start + new VectorD(0, 0.125, 0), player.Position);
        Assert.DoesNotContain(watching.SentPackets, IsPositionUpdate);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task VehicleMovesQueuedBeforeOrDuringTeleportCannotMoveTheBoat()
    {
        await using var context = Create();
        var player = AddPlayer(context, "Rider");
        var boat = new Boat { Level = context.World, EntityId = Server.GetNextEntityId(), Position = start };
        Assert.True(context.World.TrySpawnEntity(boat, hostOwned: true));
        Assert.True(player.Ride(boat));
        var client = (TestClient)player.Client;

        await VehicleMove(start + new VectorD(1, 0, 0)).HandleAsync(context.Server, player);
        await player.SendPositionAsync(VectorD.Zero);
        await VehicleMove(start + new VectorD(2, 0, 0)).HandleAsync(context.Server, player);
        await boat.TickAsync();
        Assert.Equal(start.X, boat.Position.X);

        await client.ConfirmTeleportAsync();
        var next = boat.Position + new VectorD(0.5, 0, 0);
        await VehicleMove(next).HandleAsync(context.Server, player);
        await boat.TickAsync();
        Assert.Equal(next.X, boat.Position.X);

        // A vehicle tick that already took a move can finish after a teleport and its confirmation.
        await VehicleMove(boat.Position).HandleAsync(context.Server, player);
        boat.ClientControl.Tick(boat, new Entities.AI.MobTerrain(context.World), player, flying: false);
        var destination = player.Position + new VectorD(0, 0.125, 0);
        await player.SendPositionAsync(VectorD.Zero, position: destination);
        await client.ConfirmTeleportAsync();
        boat.PositionPassengers();
        Assert.Equal(destination, player.Position);
    }

    private static RealServerContext Create()
    {
        var context = TestServerFactory.CreateServer($"pending-teleport-{Guid.NewGuid():N}");
        var world = context.World;
        world.LoadRegionByChunk(0, 0).SetChunk(new Chunk(0, 0, world.MinY, world.Height, ChunkGenStage.full));
        return context;
    }

    private static Entities.Player AddPlayer(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, start);
        player.GameMode = GameMode.Creative;
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(context.World.TryAddPlayer(player));
        Assert.True(context.World.TryAddEntity(player));
        return player;
    }

    private static bool IsPositionUpdate(IClientboundPacket packet) =>
        packet is MoveEntityPosPacket or MoveEntityPosRotPacket or EntityPositionSyncPacket or TeleportEntityPacket;

    private static T Read<T>(Action<NetworkBuffer> write) where T : ServerboundPacket, new()
    {
        using var buffer = new NetworkBuffer();
        write(buffer);
        buffer.Offset = 0;
        var packet = new T();
        packet.Populate(buffer);
        return packet;
    }

    private static MovePlayerPosPacket Move(VectorD position) => Read<MovePlayerPosPacket>(buffer =>
    {
        VectorD.Write(position, buffer);
        buffer.WriteByte(0);
    });

    private static MovePlayerPosRotPacket MoveWithRotation(VectorD position, float yaw, float pitch) =>
        Read<MovePlayerPosRotPacket>(buffer =>
        {
            VectorD.Write(position, buffer);
            buffer.WriteSingle(yaw);
            buffer.WriteSingle(pitch);
            buffer.WriteByte(0);
        });

    private static MovePlayerRotPacket Rotate(float yaw, float pitch) => Read<MovePlayerRotPacket>(buffer =>
    {
        buffer.WriteSingle(yaw);
        buffer.WriteSingle(pitch);
        buffer.WriteByte(0);
    });

    private static MovePlayerStatusOnlyPacket Status(MovementFlags flags) =>
        Read<MovePlayerStatusOnlyPacket>(buffer => buffer.WriteByte((byte)flags));

    private static MoveVehiclePacket VehicleMove(VectorD position) => Read<MoveVehiclePacket>(buffer =>
    {
        VectorD.Write(position, buffer);
        buffer.WriteSingle(0);
        buffer.WriteSingle(0);
        buffer.WriteBoolean(false);
    });
}
