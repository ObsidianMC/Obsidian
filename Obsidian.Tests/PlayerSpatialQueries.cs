#nullable enable annotations

using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

// These compare Obsidian's indexing and concurrent publication against its original brute-force queries;
// they do not prescribe gameplay rules.
public sealed class PlayerSpatialQueries
{
    private static readonly MethodInfo SnapshotMethod = typeof(AbstractLevel)
        .GetMethod("SnapshotPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task IndexedQueriesMatchBruteForceThroughMovesAndGameModes()
    {
        await using var context = TestServerFactory.CreateServer($"player-grid-{Guid.NewGuid():N}");
        var world = context.World;
        var players = Enumerable.Range(0, 40).Select(index =>
            TestServerFactory.CreateConnectedPlayer(context.Server, world, $"Player{index}",
                new VectorD((index % 8 - 4) * 64, index * 17, (index / 8 - 2) * 64))).ToArray();
        foreach (var player in players)
            Assert.True(world.TryAddPlayer(player));

        var bot = new ServerPlayer(Guid.NewGuid(), "ServerBot", context.Server, world);
        bot.Position = new VectorD(-0.01, 3000, -64);
        Assert.True(world.TryAddPlayer(bot));
        var other = new FakePlayer { Uuid = Guid.NewGuid(), Position = new VectorD(64, 64, 64) };
        Assert.True(world.TryAddPlayer(other));

        var snapshot = Snapshot(world);
        AssertEquivalent(world, snapshot);

        // In-column movement, either side of negative boundaries, and a teleport far outside the old buckets.
        VectorD[] positions =
        [
            new(0, 0, 0),
            new(-0.01, 1200, -63.99),
            new(-64, 0, -64),
            new(-64.01, -200, 64.01),
            new(-1e-200, 123, 0),
            new(1_000_000, 30, -1_000_000),
            new(1e20, 0, -1e20),
            new(double.PositiveInfinity, 0, 0),
            new(double.NaN, 0, 0),
            new(0, 0, 0)
        ];
        foreach (var position in positions)
        {
            players[0].Position = position;
            bot.Position = position;
            other.Position = position;
            AssertEquivalent(world, snapshot);
        }

        foreach (var gameMode in Enum.GetValues<GameMode>())
        {
            foreach (var player in players)
                player.GameMode = gameMode;
            bot.GameMode = gameMode;
            other.GameMode = gameMode;
            AssertEquivalent(world, snapshot);
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task QueriesPreserveSnapshotOrderAndMembershipInvalidation()
    {
        await using var source = TestServerFactory.CreateServer($"player-grid-source-{Guid.NewGuid():N}");
        await using var destination = TestServerFactory.CreateServer($"player-grid-target-{Guid.NewGuid():N}");
        var world = source.World;
        var players = Enumerable.Range(0, 16).Select(index =>
            TestServerFactory.CreateConnectedPlayer(source.Server, world, $"Player{index}",
                new VectorD(index % 2 == 0 ? -64 : 64, 0, 0))).ToArray();
        foreach (var player in players)
            Assert.True(world.TryAddPlayer(player));

        var snapshot = Snapshot(world);
        AssertEquivalent(world, snapshot);
        Assert.Same(snapshot[0], world.GetPlayersInRange(VectorD.Zero, 64).First());
        Assert.Same(snapshot.MinBy(player => player.Position.MagnitudeSquared()),
            world.GetPlayersInRange(VectorD.Zero, 64).MinBy(player => player.Position.MagnitudeSquared()));

        // Direct dictionary edits retain the old tick snapshot semantics: range queries check instance identity,
        // whereas the nearest-distance helper only checks whether that UUID is still present.
        var replacement = TestServerFactory.CreateConnectedPlayer(source.Server, world, "Replacement",
            VectorD.Zero, players[0].Uuid);
        world.Players[replacement.Uuid] = replacement;
        AssertEquivalent(world, snapshot);
        Assert.False(world.TryRemovePlayer(players[0]));

        // A real join invalidates the snapshot immediately, including its order, until the next tick.
        var joined = TestServerFactory.CreateConnectedPlayer(source.Server, world, "Joined", VectorD.Zero);
        Assert.True(world.TryAddPlayer(joined));
        AssertEquivalent(world, null);
        Assert.Contains(joined, world.GetPlayersInRange(VectorD.Zero, 1));

        Assert.True(world.TryRemovePlayer(players[1]));
        players[1].Level = destination.World;
        Assert.True(destination.World.TryAddPlayer(players[1]));
        players[1].Position = VectorD.Zero;
        AssertEquivalent(world, null);
        AssertEquivalent(destination.World, Snapshot(destination.World));
        Assert.DoesNotContain(players[1], world.GetPlayersInRange(VectorD.Zero, float.MaxValue));

        // The next snapshot incorporates replacements and removals, with the dictionary's current order.
        AssertEquivalent(world, Snapshot(world));
        Assert.True(world.TryRemovePlayer(joined));
        AssertEquivalent(world, null);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentPositionWritesPublishWholePositionsAndBuckets()
    {
        await using var context = TestServerFactory.CreateServer($"player-grid-concurrent-{Guid.NewGuid():N}");
        var world = context.World;
        var first = new VectorD(0, 0, 1024);
        var second = new VectorD(1024, 0, 0);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Moving", first);
        Assert.True(world.TryAddPlayer(player));
        var snapshot = Snapshot(world);
        using var stop = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var writer = Task.Run(() =>
        {
            started.Set();
            while (!stop.IsCancellationRequested)
            {
                player.Position = first;
                player.Position = second;
            }
        });

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            for (var index = 0; index < 1000; index++)
            {
                Assert.Empty(world.GetPlayersInRange(VectorD.Zero, 1));
                Assert.Equal(1024d * 1024, world.NearestPlayerDistanceSquared(VectorD.Zero));

                // At a stable checkpoint, compare the entire ordered result and exact nearest distance with
                // brute force. Between checkpoints the writer crosses 16 columns on every position write.
                lock (PlayerGrid.Gate)
                    AssertEquivalent(world, snapshot);
            }
        }
        finally
        {
            stop.Cancel();
            await writer;
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RangeBroadcastsKeepCapturedRecipientsAndCheckMembershipAtDispatch()
    {
        await using var context = TestServerFactory.CreateServer($"player-grid-packets-{Guid.NewGuid():N}");
        var world = context.World;
        var range = world.Configuration.EntityBroadcastRangePercentage;
        var near = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Near", VectorD.Zero);
        var departed = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Departed", VectorD.Zero);
        var excluded = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Excluded", VectorD.Zero);
        var far = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Far", new VectorD(range + 1, 0, 0));
        foreach (var player in new[] { near, departed, excluded, far })
            Assert.True(world.TryAddPlayer(player));
        Snapshot(world);

        using var broadcaster = new PacketBroadcaster(context.Server, NullLogger<PacketBroadcaster>.Instance,
            new FakeServerEnvironment());
        var immediate = new RotateHeadPacket { EntityId = 1, HeadYaw = 45 };
        broadcaster.BroadcastToLevelInRange(world, VectorD.Zero, immediate, excluded.EntityId);
        Assert.Contains(immediate, ((TestClient)near.Client).SentPackets);
        Assert.Contains(immediate, ((TestClient)departed.Client).SentPackets);
        Assert.DoesNotContain(immediate, ((TestClient)excluded.Client).SentPackets);
        Assert.DoesNotContain(immediate, ((TestClient)far.Client).SentPackets);

        // The queue captures range now. Moving out afterwards still receives it, moving in afterwards does not;
        // leaving the level before dispatch removes the recipient even though their id was captured.
        var queued = new RotateHeadPacket { EntityId = 1, HeadYaw = 90 };
        broadcaster.QueuePacketToLevelInRange(world, VectorD.Zero, queued, excluded.EntityId);
        near.Position = new VectorD(range + 1, 0, 0);
        far.Position = VectorD.Zero;
        Assert.True(world.TryRemovePlayer(departed));
        await broadcaster.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!((TestClient)near.Client).SentPackets.Contains(queued))
                await Task.Delay(10, timeout.Token);
        }
        finally
        {
            await broadcaster.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(queued, ((TestClient)departed.Client).SentPackets);
        Assert.DoesNotContain(queued, ((TestClient)excluded.Client).SentPackets);
        Assert.DoesNotContain(queued, ((TestClient)far.Client).SentPackets);
    }

    private static IPlayer[] Snapshot(AbstractLevel world)
    {
        var players = world.Players.Values.ToArray();
        SnapshotMethod.Invoke(world, null);
        return players;
    }

    private static void AssertEquivalent(AbstractLevel world, IPlayer[]? snapshot)
    {
        VectorD[] origins =
        [
            VectorD.Zero, new(-64, 64, -64), new(64, 3000, 64), new(1_000_000, 30, -1_000_000),
            new(double.NaN, 0, 0), new(double.PositiveInfinity, 0, 0)
        ];
        float[] ranges =
        [
            -1, float.NaN, 0, float.Epsilon, 1, 64, 64.01f, 192, 2000, float.MaxValue, float.PositiveInfinity
        ];
        foreach (var origin in origins)
        {
            foreach (var range in ranges)
            {
                var candidates = range == 0 || snapshot is null ? world.Players.Values.ToArray() : snapshot;
                var expected = BruteRange(world, candidates, origin, range, snapshot is not null).ToArray();
                var actual = world.GetPlayersInRange(origin, range).ToArray();
                Assert.Equal(expected, actual);
            }

            var nearest = double.MaxValue;
            foreach (var player in snapshot ?? world.Players.Values.ToArray())
            {
                if (player.GameMode != GameMode.Spectator && world.Players.ContainsKey(player.Uuid))
                    nearest = Math.Min(nearest, (player.Position - origin).MagnitudeSquared());
            }
            Assert.Equal(nearest, world.NearestPlayerDistanceSquared(origin));
        }
    }

    private static IEnumerable<IPlayer> BruteRange(AbstractLevel world, IEnumerable<IPlayer> players,
        VectorD origin, float range, bool snapshot)
    {
        if (float.IsNaN(range) || range < 0)
            yield break;

        var squared = range * range;
        foreach (var player in players)
        {
            if (range == 0)
            {
                if (player.Position == origin)
                    yield return player;
                continue;
            }

            var difference = player.Position - origin;
            var member = !snapshot || world.Players.TryGetValue(player.Uuid, out var current)
                && ReferenceEquals(current, player);
            if (difference.X * difference.X + difference.Z * difference.Z <= squared && member)
                yield return player;
        }
    }
}
