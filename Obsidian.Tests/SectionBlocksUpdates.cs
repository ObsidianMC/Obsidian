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
    public async Task PausedPassHoldsTheAcknowledgementOfAUseFinishingAfterItsLevelFlushed()
    {
        await using var context = TestServerFactory.CreateServer($"section-paused-{Guid.NewGuid():N}");
        var nether = LevelTransfers.AddNether(context);
        LevelTransfers.AddEmptyChunk(nether);
        var (player, client) = await PrepareAsync(context);
        var placed = Target + Vector.Up;

        // A nether player whose send holds the pass after the overworld's flush, before it visits the connections.
        var (entered, release) = (Gate(), Gate());
        var blocker = BlockOnFirstBlockPacket(context, nether, entered, release);
        nether.BlockUpdates.SetBlock(new Vector(1, 64, 1), BlocksRegistry.Stone);
        Assert.True(await context.Server.PauseAsync());

        Task use;
        Task pass;
        using (await player.Inventory.LockAsync())
        {
            use = BlockUpdates.UseOn(Target, BlockFace.Up, sequence: 7).HandleAsync(context.Server, player).AsTask();
            Assert.False(use.IsCompleted);
            pass = Task.Run(() => context.Server.TickConnectionsAsync(sendKeepAlive: false));
            await entered.Task;
        }

        // The use finishes after the overworld was flushed: its change waits for the next pass, and so must its
        // acknowledgement.
        await use;
        client.ClearPackets();
        release.SetResult();
        await pass;
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
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

        // The overworld's flush is stuck in a send; the paused pass still flushes the nether.
        var stuck = Task.Run(world.FlushBlockChanges);
        await entered.Task;
        await Task.Run(() => context.Server.TickConnectionsAsync(sendKeepAlive: false));
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
        resume.SetResult();
        await client.FlushAsync();
        var marker = CustomPayloadPacket.ClientboundPlay with
        {
            Channel = "test:marker",
            PluginData = ReadOnlyMemory<byte>.Empty,
        };
        Assert.True(client.SendPacket(marker));

        // An overworld block, written between the nether's respawn and the overworld's, would land in the nether.
        var bodies = await ReadFramesUntilAsync(peer.GetStream(), Body(marker));
        var respawn = bodies.FindIndex(body => body[0] == RespawnPacket.PacketId);
        Assert.True(respawn >= 0, "The client wasn't sent the respawn.");
        foreach (var position in changed)
        {
            var block = Body(new BlockUpdatePacket(position, BlocksRegistry.Dirt.GetHashCode()));
            Assert.DoesNotContain(bodies.Skip(respawn + 1), body => body.AsSpan().SequenceEqual(block));
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
    public async Task ChangesAndAcknowledgementsArrivingDuringAFlushsLastSendStillGoOut()
    {
        await using var context = TestServerFactory.CreateServer($"section-stranded-{Guid.NewGuid():N}");
        var world = context.World;
        var (player, client) = await PrepareAsync(context);
        var (entered, release) = (Gate(), Gate());
        BlockOnFirstBlockPacket(context, world, entered, release);
        world.BlockUpdates.SetBlock(new Vector(1, 64, 1), BlocksRegistry.Stone);
        var flush = Task.Run(world.FlushBlockChanges);
        await entered.Task;

        // While the flush sends the last of what it took, a change and an acknowledgement come in. Nothing flushes
        // again (the server could be paused, its ticks stopped), so the flush under way must send them.
        var changed = new Vector(3, 64, 3);
        world.BlockUpdates.SetBlock(changed, BlocksRegistry.Dirt);
        Acknowledge(world, player, 9);
        release.SetResult();
        await flush;

        AssertChangeThenAcknowledgement(client, changed, BlocksRegistry.Dirt, 9);
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
    private static void Acknowledge(AbstractLevel level, Entities.Player player, int sequence) =>
        level.AcknowledgeAfterBlockChanges(player, new Entities.BlockAcknowledgement(sequence, player.LevelEpoch));

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
