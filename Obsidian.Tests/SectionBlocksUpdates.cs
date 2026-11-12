using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Events.EventArgs;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.Utilities.Collections;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockNotifications;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Block changes go out once per tick, per chunk section, like vanilla's chunk broadcast; prediction acknowledgements
/// go out behind them. <see cref="VanillaBlockChangePackets"/> replays vanilla's order; these cover what it can't.
/// </summary>
public sealed class SectionBlocksUpdates
{
    private static readonly Vector Target = new(8, 64, 8);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ChangesOutsideTheTickGoOutAtTheNextFlushGroupedBySection()
    {
        await using var context = TestServerFactory.CreateServer($"section-updates-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        WorldBlockEntities.AddEmptyChunk(world, 1, 0);
        await world.GetBlockAsync(Target);
        var (player, client) = Join(context, new VectorD(8.5, 80, 8.5));
        var stranger = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Stranger");
        Assert.True(world.TryAddPlayer(stranger));
        stranger.LoadedChunks.Add(NumericsHelper.IntsToLong(1, 0));

        // A plugin's writes, outside any tick: two in the section above, and one in the section below, stored twice.
        await world.SetBlockAsync(new Vector(1, 81, 2), BlocksRegistry.Stone);
        await world.SetBlockAsync(new Vector(3, 64, 4), BlocksRegistry.Dirt);
        await world.SetBlockAsync(new Vector(5, 82, 6), BlocksRegistry.Stone);
        await world.SetBlockAsync(new Vector(3, 64, 4), BlocksRegistry.Cobblestone);
        Assert.Empty(BlockUpdates.SentBlocks(client));

        world.FlushBlockChanges();

        var packets = client.SentPackets.Where(packet => packet is BlockUpdatePacket or SectionBlocksUpdatePacket)
            .ToArray();
        Assert.Equal(2, packets.Length);
        var below = Assert.IsType<BlockUpdatePacket>(packets[0]);
        Assert.Equal((new Vector(3, 64, 4), BlocksRegistry.Cobblestone.GetHashCode()), (below.Position, below.BlockId));
        var above = Assert.IsType<SectionBlocksUpdatePacket>(packets[1]);
        Assert.Equal((0, 5, 0), (above.SectionX, above.SectionY, above.SectionZ));
        var stone = BlocksRegistry.Stone.GetHashCode();
        Assert.Equal(
            [(new Vector(1, 81, 2), stone), (new Vector(5, 82, 6), stone)],
            above.Blocks().OrderBy(block => block.Position.X).ToArray());
        Assert.Empty(BlockUpdates.SentBlocks((TestClient)stranger.Client));

        // Each change goes out once.
        client.ClearPackets();
        world.FlushBlockChanges();
        Assert.Empty(BlockUpdates.SentBlocks(client));
        Assert.Same(player, client.Player);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AcknowledgementWaitsForTheWorldTickThatSendsItsChanges()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gated = false;
        World? ticked = null;
        await using var context = TestServerFactory.CreateServer($"section-acks-{Guid.NewGuid():N}",
            world => new FakeWorldManager(world)
            {
                TickWorlds = async () =>
                {
                    if (gated)
                    {
                        entered.SetResult();
                        await release.Task;
                    }

                    await ticked!.DoWorldTickAsync();
                },
            });
        var world = ticked = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Target);
        world.BlockUpdates.SetBlock(Target, BlocksRegistry.Stone);
        var (player, client) = Join(context, new VectorD(8.5, 64, 10.5));
        var placed = Target + Vector.Up;

        // A use between ticks: its change and acknowledgement wait for the next world tick, change first.
        await BlockUpdates.UseOn(Target, BlockFace.Up, sequence: 3).HandleAsync(context.Server, player);
        client.ClearPackets();
        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        await context.Server.TickWorldsOnceAsync();
        AssertChangeThenAcknowledgement(client, placed, BlocksRegistry.Stone, 3);

        // A use received before a world tick began and finishing after it missed the tick's flush: it and its
        // acknowledgement go out with the next. The held inventory keeps the use waiting until the tick is over.
        Task use;
        using (await player.Inventory.LockAsync())
        {
            use = BlockUpdates.UseOn(placed, BlockFace.Up, sequence: 4).HandleAsync(context.Server, player).AsTask();
            Assert.False(use.IsCompleted);
            gated = true;
            var tick = context.Server.TickWorldsOnceAsync();
            await entered.Task;
            gated = false;
            release.SetResult();
            await tick;
        }

        await use;
        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        await context.Server.TickWorldsOnceAsync();
        AssertChangeThenAcknowledgement(client, placed + Vector.Up, BlocksRegistry.Stone, 4);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PausedTurnHoldsTheAcknowledgementOfAUseFinishingAfterItsLevelFlushed()
    {
        await using var context = TestServerFactory.CreateServer($"section-paused-{Guid.NewGuid():N}");
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        var (player, client) = await PrepareAsync(context);
        var placed = Target + Vector.Up;

        // A nether player whose send holds the paused turn after the overworld's flush.
        var (entered, release) = (Gate(), Gate());
        var blocker = BlockOnFirstBlockPacket(context, nether, entered, release);
        nether.BlockUpdates.SetBlock(new Vector(1, 64, 1), BlocksRegistry.Stone);
        Assert.True(await context.Server.PauseAsync());

        Task use;
        Task turn;
        using (await player.Inventory.LockAsync())
        {
            use = BlockUpdates.UseOn(Target, BlockFace.Up, sequence: 7).HandleAsync(context.Server, player).AsTask();
            Assert.False(use.IsCompleted);
            turn = Task.Run(context.Server.RunTickTurnAsync);
            await entered.Task;
        }

        // The use finishes after the overworld was flushed: its change waits for the next turn, and so must its
        // acknowledgement.
        await use;
        client.ClearPackets();
        release.SetResult();
        await turn;
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        await context.Server.RunTickTurnAsync();
        AssertChangeThenAcknowledgement(client, placed, BlocksRegistry.Stone, 7);
        Assert.Contains((new Vector(1, 64, 1), BlocksRegistry.Stone.GetHashCode()),
            BlockUpdates.SentBlocks((TestClient)blocker.Client));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerLeavingForAnotherLevelMidFlushGetsNoBlocksOfTheLevelLeft()
    {
        await using var context = TestServerFactory.CreateServer($"section-transfer-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        var (player, client) = await PrepareAsync(context);
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);

        // The flush has chosen the player and is about to send when the player leaves for the nether.
        var (entered, release) = (Gate(), Gate());
        client.BeforeSend = packet =>
        {
            if (packet is BlockUpdatePacket or SectionBlocksUpdatePacket && entered.TrySetResult())
                release.Task.Wait();
        };
        var flush = Task.Run(world.FlushBlockChanges);
        await entered.Task;
        client.BeforeSend = null;
        await player.TeleportAsync(nether, new VectorD(8, 70, 8));
        Assert.Same(nether, player.Level);
        release.SetResult();
        await flush;

        // Block packets name no level: one arriving after the respawn would change the nether's block there.
        var packets = client.SentPackets.ToList();
        var respawn = packets.FindLastIndex(packet => packet is RespawnPacket);
        Assert.True(respawn >= 0, "The player wasn't sent the respawn.");
        var after = packets.Skip(respawn + 1).SelectMany(BlockUpdates.SentBlocksOf);
        Assert.DoesNotContain(after, block => block.Position == changed);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SteadyUseFinishingDuringEveryTickIsStillAcknowledged()
    {
        TickGate? gate = null;
        await using var context = CreateTickingServer("section-steady", () => gate?.PassAsync() ?? Task.CompletedTask);
        var (player, client) = await PrepareAsync(context);

        // Each use was received before a world tick began and finishes during it, before the tick's flush.
        for (var use = 0; use < 5; use++)
        {
            var clicked = Target + new Vector(0, use, 0);
            var sequence = 10 + use;
            Task handling;
            Task tick;
            using (await player.Inventory.LockAsync())
            {
                handling = BlockUpdates.UseOn(clicked, BlockFace.Up, sequence: sequence)
                    .HandleAsync(context.Server, player).AsTask();
                Assert.False(handling.IsCompleted);
                gate = new TickGate();
                tick = context.Server.TickWorldsOnceAsync();
                await gate.Entered.Task;
            }

            await handling;
            client.ClearPackets();
            gate.Release.SetResult();
            await tick;
            gate = null;
            AssertChangeThenAcknowledgement(client, clicked + Vector.Up, BlocksRegistry.Stone, sequence);
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AWorldTickFailingBeforeItsFlushReleasesNoAcknowledgement()
    {
        var failing = false;
        await using var context = CreateTickingServer("section-failing", () => failing
            ? Task.FromException(new InvalidOperationException("Injected tick failure"))
            : Task.CompletedTask);
        var (player, client) = await PrepareAsync(context);
        var placed = Target + Vector.Up;

        await BlockUpdates.UseOn(Target, BlockFace.Up, sequence: 5).HandleAsync(context.Server, player);
        client.ClearPackets();
        failing = true;
        await context.Server.TickWorldsOnceAsync();
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        failing = false;
        await context.Server.TickWorldsOnceAsync();
        AssertChangeThenAcknowledgement(client, placed, BlocksRegistry.Stone, 5);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ASendBlockedInOneLevelsFlushDoesNotStallAnotherLevels()
    {
        await using var context = TestServerFactory.CreateServer($"section-blocked-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Target);
        var (entered, release) = (Gate(), Gate());
        BlockOnFirstBlockPacket(context, world, entered, release);
        var viewer = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Viewer", new VectorD(8, 70, 8));
        viewer.Level = nether;
        Assert.True(nether.TryAddPlayer(viewer));
        viewer.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        world.BlockUpdates.SetBlock(Target, BlocksRegistry.Stone);
        nether.BlockUpdates.SetBlock(new Vector(1, 64, 1), BlocksRegistry.Stone);
        Assert.True(await context.Server.PauseAsync());

        // The overworld's flush is stuck in a send; the paused turn still flushes the nether.
        var stuck = Task.Run(world.FlushBlockChanges);
        await entered.Task;
        await Task.Run(context.Server.RunTickTurnAsync);
        Assert.Contains((new Vector(1, 64, 1), BlocksRegistry.Stone.GetHashCode()),
            BlockUpdates.SentBlocks((TestClient)viewer.Client));

        release.SetResult();
        await stuck;
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ABlockPacketHeldInTheOutputIsNotSentAfterALevelChangeAndBack()
    {
        await using var context = TestServerFactory.CreateServer($"section-held-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Target);
        world.FlushBlockChanges();
        var key = NumericsHelper.IntsToLong(0, 0);

        // A real connection, whose output the test reads back.
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new TcpClient();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var client = new Client(
            context.EventDispatcher, context.Server, NullLoggerFactory.Instance, new FakeUserCache(),
            new ServerMetrics(metrics.GetRequiredService<IMeterFactory>()), context.Provider,
            new SimpleObjectPool<SocketAsyncEventArgs>(8));
        client.SetState(ClientState.Play);
        await client.ConnectAsync(await listener.AcceptSocketAsync());
        var player = new Entities.Player(Guid.NewGuid(), "Traveller", client, world)
        {
            Server = context.Server,
            Position = new VectorD(8.5, 64, 10.5),
        };
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        player.LoadedChunks.Add(key);

        // The connection is as a join leaves it: the player is the client's, and its login put it in the overworld.
        typeof(Client).GetProperty(nameof(Client.Player))!.SetValue(client, player);
        await client.QueuePacketAsync(new LoginPacket
        {
            EntityId = player.EntityId,
            DimensionNames = ["minecraft:overworld", "minecraft:the_nether"],
            CommonPlayerSpawnInfo = player.CreateSpawnInfo(),
        });

        // Before it leaves, the player gets the overworld's blocks.
        var stone = BlocksRegistry.Stone.GetHashCode();
        var before = new Vector(5, 64, 5);
        world.BlockUpdates.SetBlock(before, BlocksRegistry.Stone);
        world.FlushBlockChanges();

        // A plugin's handler holds the client's pipeline: what's queued from here waits, in order.
        var hold = CustomPayloadPacket.ClientboundPlay with
        {
            Channel = "test:hold",
            PluginData = ReadOnlyMemory<byte>.Empty,
        };
        var (held, resume) = (Gate(), Gate());
        context.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(async args =>
        {
            if (ReferenceEquals(args.Packet, hold))
            {
                held.TrySetResult();
                await resume.Task;
            }
        }));
        await client.QueuePacketAsync(hold);
        await held.Task;

        // The overworld's flush has chosen its recipients when another player's send holds it. It chooses them once
        // for the chunk and sends its sections in turn: whichever player it visits first, the traveller's packet for
        // the upper section goes out after the transfer.
        Vector[] changed = [new(3, 64, 3), new(3, 80, 3)];
        foreach (var position in changed)
            world.BlockUpdates.SetBlock(position, BlocksRegistry.Dirt);

        var (entered, release) = (Gate(), Gate());
        BlockOnFirstBlockPacket(context, world, entered, release);
        var flush = Task.Run(world.FlushBlockChanges);
        await entered.Task;

        // The player leaves for the nether, the flush goes on, and the player comes back before the output drains.
        await player.TeleportAsync(nether, new VectorD(8, 70, 8));
        release.SetResult();
        await flush;
        await player.TeleportAsync(world, new VectorD(8.5, 64, 10.5));
        player.LoadedChunks.Add(key);

        // Back in the overworld, behind its respawn, the player gets its blocks again.
        var back = new Vector(6, 64, 6);
        world.BlockUpdates.SetBlock(back, BlocksRegistry.Stone);
        world.FlushBlockChanges();
        resume.SetResult();
        await client.FlushAsync();
        var marker = CustomPayloadPacket.ClientboundPlay with
        {
            Channel = "test:marker",
            PluginData = ReadOnlyMemory<byte>.Empty,
        };
        Assert.True(client.SendPacket(marker));

        var bodies = await ReadFramesUntilAsync(peer.GetStream(), Body(marker));
        var respawns = bodies.Select((body, index) => (body, index))
            .Where(frame => frame.body[0] == RespawnPacket.PacketId)
            .Select(frame => frame.index)
            .ToList();
        Assert.True(respawns.Count >= 2, "The client wasn't sent both respawns.");
        int Find(Vector position, int block) =>
            bodies.FindIndex(body => body.AsSpan().SequenceEqual(Body(new BlockUpdatePacket(position, block))));

        var sentBefore = Find(before, stone);
        Assert.True(sentBefore >= 0 && sentBefore < respawns[0], "The overworld's block didn't come before leaving.");
        Assert.True(Find(back, stone) > respawns[^1], "The overworld's block didn't come after the return.");

        // An overworld block, written between the nether's respawn and the overworld's, would land in the nether.
        foreach (var position in changed)
        {
            var block = Body(new BlockUpdatePacket(position, BlocksRegistry.Dirt.GetHashCode()));
            Assert.DoesNotContain(bodies.Skip(respawns[0] + 1), body => body.AsSpan().SequenceEqual(block));
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ABlockPacketCheckedAfterARespawnIsDroppedThoughThePlayerIsBackInItsLevel()
    {
        await using var context = TestServerFactory.CreateServer($"section-epoch-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        var (player, client) = await PrepareAsync(context);
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);

        // The flush has chosen the player and is about to send.
        var (entered, release) = (Gate(), Gate());
        client.BeforeSend = packet =>
        {
            if (packet is BlockUpdatePacket or SectionBlocksUpdatePacket && entered.TrySetResult())
                release.Task.Wait();
        };
        var flush = Task.Run(world.FlushBlockChanges);
        await entered.Task;
        client.BeforeSend = null;

        // The player left for the nether and is on the way back: the return has set the level but not yet queued its
        // respawn, and the chunk the player has is still the nether's.
        await player.TeleportAsync(nether, new VectorD(8, 70, 8));
        player.Level = world;
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        release.SetResult();
        await flush;

        var packets = client.SentPackets.ToList();
        var respawn = packets.FindLastIndex(packet => packet is RespawnPacket);
        Assert.True(respawn >= 0, "The player wasn't sent the respawn.");
        var after = packets.Skip(respawn + 1).SelectMany(BlockUpdates.SentBlocksOf);
        Assert.DoesNotContain(after, block => block.Position == changed);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ChangesAndAcknowledgementsArrivingDuringAFlushGoOutWithTheNext()
    {
        await using var context = TestServerFactory.CreateServer($"section-stranded-{Guid.NewGuid():N}");
        var world = context.World;
        var (player, client) = await PrepareAsync(context);
        var (entered, release) = (Gate(), Gate());
        BlockOnFirstBlockPacket(context, world, entered, release);
        world.BlockUpdates.SetBlock(new Vector(1, 64, 1), BlocksRegistry.Stone);
        var flush = Task.Run(world.FlushBlockChanges);
        await entered.Task;

        // While the flush sends what it took, a change and an acknowledgement come in, and another flush, finding
        // this one under way, returns at once. The flush under way sends only what it took.
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        Acknowledge(world, player, 9);
        world.FlushBlockChanges();
        release.SetResult();
        await flush;
        Assert.DoesNotContain((changed, BlocksRegistry.Dirt.GetHashCode()), BlockUpdates.SentBlocks(client));
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        // Nothing is lost: it's still pending when the flush ends, and the next flush (the next tick's, or the next
        // paused turn's) sends it.
        world.FlushBlockChanges();
        AssertChangeThenAcknowledgement(client, changed, BlocksRegistry.Dirt, 9);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AFlushEndsThoughChangesKeepComingWhileItSends()
    {
        await using var context = TestServerFactory.CreateServer($"section-endless-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Target);
        world.FlushBlockChanges();
        var (player, client) = Join(context, new VectorD(8.5, 64, 10.5));

        // Every block packet sent records another change, as a writer that never stops would. It stops after 100,
        // so that a flush that doesn't end on its own still ends here.
        var sends = 0;
        client.BeforeSend = packet =>
        {
            if (packet is BlockUpdatePacket or SectionBlocksUpdatePacket && ++sends < 100)
                world.BlockChanged(Target);
        };
        world.BlockChanged(Target);
        world.FlushBlockChanges();
        Assert.Equal(1, sends);

        // The change recorded meanwhile waits for the next flush.
        world.FlushBlockChanges();
        Assert.Equal(2, sends);
        Assert.Same(player, client.Player);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ADestinationsBlocksWaitForTheRespawnThatTakesThePlayerThere()
    {
        await using var context = TestServerFactory.CreateServer($"section-arrival-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        var (player, client) = await PrepareAsync(context);
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        await player.TeleportAsync(nether, new VectorD(8, 70, 8));
        var key = NumericsHelper.IntsToLong(0, 0);
        player.LoadedChunks.Add(key);

        // The player is on the way back to the overworld: the transfer has made it their level and registered them
        // there, but hasn't queued the respawn yet, and their chunks are still the nether's.
        player.Level = world;
        Assert.True(world.TryAddPlayer(player));
        Assert.Contains(key, player.LoadedChunks);

        // The overworld flushes in that window: its block would change the nether's on the client.
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        world.FlushBlockChanges();

        var packets = client.SentPackets.ToList();
        var respawn = packets.FindLastIndex(packet => packet is RespawnPacket);
        Assert.True(respawn >= 0, "The player wasn't sent the respawn.");
        var after = packets.Skip(respawn + 1).SelectMany(BlockUpdates.SentBlocksOf);
        Assert.DoesNotContain(after, block => block.Position == changed);

        // Once the return's respawn is queued, the player is a recipient again.
        await client.QueuePacketAsync(new RespawnPacket { CommonPlayerSpawnInfo = player.CreateSpawnInfo() });
        client.ClearPackets();
        var arrived = new Vector(5, 64, 5);
        world.BlockUpdates.SetBlock(arrived, BlocksRegistry.Stone);
        world.FlushBlockChanges();
        Assert.Contains((arrived, BlocksRegistry.Stone.GetHashCode()), BlockUpdates.SentBlocks(client));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AFailedSendLeavesItsChangesAndAcknowledgementsForTheNextFlush()
    {
        await using var context = TestServerFactory.CreateServer($"section-failed-{Guid.NewGuid():N}");
        var world = context.World;
        var (player, client) = await PrepareAsync(context);
        var (entered, release) = (Gate(), Gate());
        var failing = (TestClient)BlockOnFirstBlockPacket(context, world, entered, release).Client;
        release.SetResult();
        var failed = false;
        failing.BeforeSend = packet =>
        {
            if (packet is BlockUpdatePacket or SectionBlocksUpdatePacket && !failed)
            {
                failed = true;
                throw new InvalidOperationException("Injected send failure");
            }
        };
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        Acknowledge(world, player, 8);

        // One recipient's send fails once, part way through the flush.
        Assert.Throws<InvalidOperationException>(world.FlushBlockChanges);

        // The next flush sends what that one took: a correction sent twice does no harm.
        world.FlushBlockChanges();
        AssertChangeThenAcknowledgement(client, changed, BlocksRegistry.Dirt, 8);
        Assert.Contains((changed, BlocksRegistry.Dirt.GetHashCode()), BlockUpdates.SentBlocks(failing));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APausedServerFlushesOnEveryTurnOfItsTickLoop()
    {
        await using var context = TestServerFactory.CreateServer($"section-turns-{Guid.NewGuid():N}");
        var world = context.World;
        var (player, client) = await PrepareAsync(context);
        Assert.True(await context.Server.PauseAsync());

        // The loop takes the tick gate every turn while paused. The flush is that turn's own work, so it can't lose
        // the gate to the loop, as a connection pass that only tried the gate could.
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        Acknowledge(world, player, 5);
        Assert.False(await context.Server.RunTickTurnAsync());
        AssertChangeThenAcknowledgement(client, changed, BlocksRegistry.Dirt, 5);

        // A connection pass sends neither: they're the loop's.
        client.ClearPackets();
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Stone);
        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        Assert.Empty(BlockUpdates.SentBlocks(client));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APausedTurnWhoseFlushFailsGoesOnAndSendsItAtTheNext()
    {
        await using var context = TestServerFactory.CreateServer($"section-turn-fails-{Guid.NewGuid():N}");
        var world = context.World;
        var (player, client) = await PrepareAsync(context);
        var (entered, release) = (Gate(), Gate());
        var failing = (TestClient)BlockOnFirstBlockPacket(context, world, entered, release).Client;
        release.SetResult();
        var failed = false;
        failing.BeforeSend = packet =>
        {
            if (packet is BlockUpdatePacket or SectionBlocksUpdatePacket && !failed)
            {
                failed = true;
                throw new InvalidOperationException("Injected send failure");
            }
        };
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        Acknowledge(world, player, 3);
        Assert.True(await context.Server.PauseAsync());

        // The failure is the turn's to report, as a failing tick's is: the loop goes on, and its next turn sends.
        Assert.False(await context.Server.RunTickTurnAsync());
        Assert.True(failed);
        Assert.False(await context.Server.RunTickTurnAsync());
        AssertChangeThenAcknowledgement(client, changed, BlocksRegistry.Dirt, 3);
        Assert.Contains((changed, BlocksRegistry.Dirt.GetHashCode()), BlockUpdates.SentBlocks(failing));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APausedTurnWaitsForTheTickHoldingTheGate()
    {
        await using var context = TestServerFactory.CreateServer($"section-stale-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        var note = new Vector(0, 72, 0);
        world.BlockUpdates.SetBlock(note, BlocksRegistry.NoteBlock,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        world.FlushBlockChanges();
        var (player, client) = Join(context, new VectorD(0.5, 72, 2.5));
        Assert.True(await context.Server.PauseAsync());

        // A tick is in its block events, holding the tick gate as the server's loop does: it has flushed its change,
        // but not yet sent the acknowledgement that follows its events. The server reads as paused, as it would to a
        // turn queued on the gate just before a resume.
        var tickGate = (SemaphoreSlim)context.Server.GetType()
            .GetField("tickGate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context.Server)!;
        await tickGate.WaitAsync();
        var changed = new Vector(3, 72, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Stone,
            BlockUpdateFlags.Clients | BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        Acknowledge(world, player, 6);
        var (entered, release) = (Gate(), Gate());
        world.QueueBlockEvent(note, BlockIds.NoteBlock, 0, 0, async () =>
        {
            entered.SetResult();
            await release.Task;
            return true;
        });
        var tick = Task.Run(world.DoWorldTickAsync);
        await entered.Task;

        // The paused turn waits for the gate, and with it for the tick's acknowledgement.
        var turn = context.Server.RunTickTurnAsync();
        Assert.False(turn.IsCompleted, "The paused turn didn't wait for the tick gate.");
        release.SetResult();
        await tick;
        tickGate.Release();
        Assert.False(await turn);

        var packets = client.SentPackets.ToList();
        var change = packets.FindIndex(packet => BlockUpdates.SentBlocksOf(packet)
            .Contains((changed, BlocksRegistry.Stone.GetHashCode())));
        var played = packets.FindIndex(packet => packet is BlockEventPacket blockEvent && blockEvent.Position == note);
        var acknowledgement = packets.FindIndex(packet => packet is BlockChangedAckPacket { SequenceID: 6 });
        Assert.True(change >= 0, "The client wasn't sent the change.");
        Assert.True(played > change, "The block event didn't follow the change.");
        Assert.True(acknowledgement > played, "The acknowledgement didn't follow the tick's block events.");
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATickFailingBeforeItsFlushStillSendsItsChangesAndThenItsAcknowledgements()
    {
        await using var context = TestServerFactory.CreateServer($"section-throws-{Guid.NewGuid():N}");
        var world = context.World;
        var (player, client) = await PrepareAsync(context);
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        Acknowledge(world, player, 4);

        // The level's tick fails in its entity actions, before its flush, as it might every tick.
        world.EnqueueEntityAction(() => throw new InvalidOperationException("Injected tick failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(world.DoWorldTickAsync);

        AssertChangeThenAcknowledgement(client, changed, BlocksRegistry.Dirt, 4);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AcknowledgementsNeverGoBackwardsAcrossLevels()
    {
        await using var context = TestServerFactory.CreateServer($"section-backwards-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        var (player, client) = await PrepareAsync(context);

        // Sequence 21 finishes in the overworld; the player moves to the nether, where 22 finishes and is flushed
        // first. The client ends its predictions up to 22, so 21 after it is stale.
        Acknowledge(world, player, 21);
        player.Level = nether;
        Acknowledge(nether, player, 22);
        nether.FlushBlockChanges();
        world.FlushBlockChanges();

        Assert.Equal([22], client.SentPackets.OfType<BlockChangedAckPacket>().Select(ack => ack.SequenceID));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AcknowledgementsStartAgainInTheLevelAPlayerChangesTo()
    {
        await using var context = TestServerFactory.CreateServer($"section-renumbered-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        var (player, client) = await PrepareAsync(context);
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        Acknowledge(world, player, 21);
        world.FlushBlockChanges();

        // Sequence 22 finishes in the overworld, but the player leaves for the nether before it's flushed. The client
        // dropped its overworld predictions with the level, and numbers the nether's afresh.
        Acknowledge(world, player, 22);
        await player.TeleportAsync(nether, new VectorD(8, 70, 8));
        Acknowledge(nether, player, 1);
        nether.FlushBlockChanges();
        world.FlushBlockChanges();

        Assert.Equal([21, 1], client.SentPackets.OfType<BlockChangedAckPacket>().Select(ack => ack.SequenceID));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnAcknowledgementFollowsTheBlockEventsOfTheTickThatSendsItsChanges()
    {
        await using var context = TestServerFactory.CreateServer($"section-events-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        var note = new Vector(0, 72, 0);
        world.BlockUpdates.SetBlock(note, BlocksRegistry.NoteBlock,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        world.FlushBlockChanges();
        var (player, client) = Join(context, new VectorD(0.5, 72, 2.5));

        // A use finished between ticks, with its change and acknowledgement, and a note block plays in the next tick.
        // Vanilla's client keeps a server's correction in its prediction cache until the acknowledgement, and runs
        // block events against the predicted block: the acknowledgement comes after the tick's events, as vanilla's
        // connection tick sends it after the levels' ticks.
        var changed = new Vector(3, 72, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Stone,
            BlockUpdateFlags.Clients | BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        Acknowledge(world, player, 6);
        world.PlayNoteBlock(note);
        await world.DoWorldTickAsync();

        var packets = client.SentPackets.ToList();
        var change = packets.FindIndex(packet => BlockUpdates.SentBlocksOf(packet)
            .Contains((changed, BlocksRegistry.Stone.GetHashCode())));
        var played = packets.FindIndex(packet => packet is BlockEventPacket blockEvent
            && blockEvent.BlockType == BlockIds.NoteBlock && blockEvent.Position == note);
        var acknowledgement = packets.FindIndex(packet => packet is BlockChangedAckPacket { SequenceID: 6 });
        Assert.True(change >= 0, "The client wasn't sent the change.");
        Assert.True(played > change, "The note event didn't follow the change.");
        Assert.True(acknowledgement > played, "The acknowledgement didn't follow the tick's block events.");
    }

    // An uncompressed frame's body, as a connection without compression writes it: the packet's id and payload.
    private static byte[] Body(IClientboundPacket packet)
    {
        using var frame = new NetworkBuffer();
        frame.WritePacket(packet);
        var bytes = frame.AsSpan(0, frame.Size).ToArray();
        var length = 1;
        while ((bytes[length - 1] & 0x80) != 0)
            length++;

        return bytes[length..];
    }

    // Reads uncompressed frames until one's body is the marker's, and returns the bodies before it.
    private static async Task<List<byte[]>> ReadFramesUntilAsync(Stream stream, byte[] marker)
    {
        var bodies = new List<byte[]>();
        while (true)
        {
            var body = new byte[await ReadVarIntAsync(stream)];
            await stream.ReadExactlyAsync(body);
            if (body.AsSpan().SequenceEqual(marker))
                return bodies;

            bodies.Add(body);
        }
    }

    private static async Task<int> ReadVarIntAsync(Stream stream)
    {
        var value = 0;
        var one = new byte[1];
        for (var shift = 0; ; shift += 7)
        {
            await stream.ReadExactlyAsync(one);
            value |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
                return value;
        }
    }

    // A finished handler's acknowledgement of sequence, received while the player's connection is in its current level.
    private static void Acknowledge(AbstractLevel level, Entities.Player player, int sequence)
    {
        var epoch = player.LevelEpoch?.Number ?? 0;
        level.AcknowledgeAfterBlockChanges(player, new Entities.BlockAcknowledgement(sequence, epoch));
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A player in level, with chunk (0, 0), whose first block packet holds its sender until release.
    private static Entities.Player BlockOnFirstBlockPacket(RealServerContext context, AbstractLevel level,
        TaskCompletionSource entered, TaskCompletionSource release)
    {
        var position = new VectorD(8, 70, 8);
        var blocker = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Blocker", position);
        blocker.Level = level;
        Assert.True(level.TryAddPlayer(blocker));
        blocker.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        ((TestClient)blocker.Client).BeforeSend = packet =>
        {
            if (packet is BlockUpdatePacket or SectionBlocksUpdatePacket && entered.TrySetResult())
                release.Task.Wait();
        };

        return blocker;
    }

    // The overworld's chunk (0, 0) with stone at the target, and a viewer beside it with nothing pending.
    private static async Task<(Entities.Player Player, TestClient Client)> PrepareAsync(RealServerContext context)
    {
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Target);
        world.BlockUpdates.SetBlock(Target, BlocksRegistry.Stone);
        world.FlushBlockChanges();
        return Join(context, new VectorD(8.5, 64, 10.5));
    }

    // A server whose world ticks run beforeTick, then the world's tick.
    private static RealServerContext CreateTickingServer(string name, Func<Task> beforeTick) =>
        TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}", world => new FakeWorldManager(world)
        {
            TickWorlds = async () =>
            {
                await beforeTick();
                await world.DoWorldTickAsync();
            },
        });

    // Holds a world tick before the world ticks: Entered once it's held, until Release.
    private sealed class TickGate
    {
        public TaskCompletionSource Entered { get; } = Gate();

        public TaskCompletionSource Release { get; } = Gate();

        public async Task PassAsync()
        {
            this.Entered.SetResult();
            await this.Release.Task;
        }
    }

    private static (Entities.Player Player, TestClient Client) Join(RealServerContext context, VectorD position)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Viewer", position);
        player.GameMode = GameMode.Creative;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Stone, 1));
        Assert.True(context.World.TryAddPlayer(player));
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));

        var client = (TestClient)player.Client;
        client.ClearPackets();
        return (player, client);
    }

    private static void AssertChangeThenAcknowledgement(TestClient client, Vector position, IBlock block, int sequence)
    {
        var packets = client.SentPackets.ToList();
        var change = packets.FindIndex(packet => packet switch
        {
            BlockUpdatePacket update => update.Position == position && update.BlockId == block.GetHashCode(),
            SectionBlocksUpdatePacket section => section.Blocks().Contains((position, block.GetHashCode())),
            _ => false,
        });
        var acknowledgement = packets.FindIndex(packet => packet is BlockChangedAckPacket ack
            && ack.SequenceID == sequence);
        Assert.True(change >= 0, "The client wasn't sent the change.");
        Assert.True(acknowledgement > change, "The acknowledgement didn't follow the change.");
        client.ClearPackets();
    }
}
