#nullable enable annotations

using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
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
    public async Task SuspendedQueriesMatchBruteForceAfterRemovalTransferAndMovement()
    {
        await FixtureCases.AssertAllAsync(Enum.GetValues<SuspendedChange>(), change => change.ToString(),
            AssertSuspendedQuery);
    }

    private enum SuspendedChange
    {
        Remove,
        Transfer,
        ReplaceInstance,
        MoveOut,
        MoveIntoSameColumn,
        MoveIntoNewColumn
    }

    private static async Task AssertSuspendedQuery(SuspendedChange change)
    {
        await using var source = TestServerFactory.CreateServer($"player-grid-suspended-{Guid.NewGuid():N}");
        await using var target = TestServerFactory.CreateServer($"player-grid-suspended-target-{Guid.NewGuid():N}");
        var world = source.World;
        var first = TestServerFactory.CreateConnectedPlayer(source.Server, world, "First", VectorD.Zero);
        var second = TestServerFactory.CreateConnectedPlayer(source.Server, world, "Second", VectorD.Zero);
        Assert.True(world.TryAddPlayer(first));
        Assert.True(world.TryAddPlayer(second));
        var snapshot = Snapshot(world);
        var pending = (PlayerBase)snapshot[1];
        if (change == SuspendedChange.MoveIntoSameColumn)
            pending.Position = new VectorD(8, 0, 0);
        if (change == SuspendedChange.MoveIntoNewColumn)
            pending.Position = new VectorD(1024, 0, 0);

        using var actual = world.GetPlayersInRange(VectorD.Zero, 1).GetEnumerator();
        using var expected = BruteRange(world, snapshot, VectorD.Zero, 1, true).GetEnumerator();
        Assert.True(expected.MoveNext());
        Assert.True(actual.MoveNext());
        Assert.Same(expected.Current, actual.Current);
        Assert.Same(snapshot[0], actual.Current);

        switch (change)
        {
            case SuspendedChange.Remove:
                Assert.True(world.TryRemovePlayer(pending));
                break;
            case SuspendedChange.Transfer:
                Assert.True(world.TryRemovePlayer(pending));
                pending.Level = target.World;
                Assert.True(target.World.TryAddPlayer(pending));
                break;
            case SuspendedChange.ReplaceInstance:
                var replacement = TestServerFactory.CreateConnectedPlayer(source.Server, world, "Replacement",
                    VectorD.Zero, pending.Uuid);
                world.Players[pending.Uuid] = replacement;
                break;
            case SuspendedChange.MoveOut:
                pending.Position = new VectorD(8, 0, 0);
                break;
            case SuspendedChange.MoveIntoSameColumn:
            case SuspendedChange.MoveIntoNewColumn:
                pending.Position = VectorD.Zero;
                break;
        }

        Assert.Equal(Drain(expected), Drain(actual));
    }

    private static IPlayer[] Drain(IEnumerator<IPlayer> iterator)
    {
        var remaining = new List<IPlayer>();
        while (iterator.MoveNext())
            remaining.Add(iterator.Current);
        return remaining.ToArray();
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentPositionWritesPublishWholePositionsAndBuckets()
    {
        await using var context = TestServerFactory.CreateServer($"player-grid-concurrent-{Guid.NewGuid():N}");
        var world = context.World;
        VectorD[] endpoints =
        [
            new(63.75, 7, -64.25), new(1024.25, 7, 63.75),
            new(64.25, 7, -63.75), new(1023.75, 7, 64.25)
        ];
        VectorD[] edges = [new(64, 7, -64), new(1024, 7, 64)];
        var queries = endpoints.Concat(edges).ToArray();
        var positions = Enumerable.Range(0, 32).SelectMany(_ => endpoints).ToArray();
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Moving", endpoints[0]);
        Assert.True(world.TryAddPlayer(player));
        var snapshot = Snapshot(world);
        using var stop = new CancellationTokenSource();
        using var published = new SemaphoreSlim(0);
        using var advance = new SemaphoreSlim(0);
        var writer = Task.Run(async () =>
        {
            foreach (var position in positions)
            {
                player.Position = position;
                published.Release();
                await advance.WaitAsync(stop.Token);
            }
        });

        try
        {
            foreach (var position in positions)
            {
                Assert.True(await published.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(position, PlayerGrid.ReadPosition(player));

                // The writer has published the position and cannot move again until every narrow query is
                // checked. These rectangles visit endpoint/edge columns, never the occupied-column fallback.
                foreach (var origin in queries)
                {
                    foreach (var range in new[] { 0.1f, 0.5f })
                    {
                        var expected = BruteRange(world, snapshot, origin, range, true).ToArray();
                        Assert.Equal(expected, world.GetPlayersInRange(origin, range).ToArray());
                    }
                    Assert.Equal((position - origin).MagnitudeSquared(), world.NearestPlayerDistanceSquared(origin));
                }
                advance.Release();
            }
        }
        finally
        {
            stop.Cancel();
            try
            {
                await writer;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Release a writer waiting at a publication checkpoint when an assertion fails.
            }
        }

        // Also query while writes are actually in flight; either complete position has the same distance.
        player.Position = new VectorD(0, 0, 1024);
        using var racingStop = new CancellationTokenSource();
        var racingWriter = Task.Run(() =>
        {
            while (!racingStop.IsCancellationRequested)
            {
                player.Position = new VectorD(0, 0, 1024);
                player.Position = new VectorD(1024, 0, 0);
            }
        });
        try
        {
            for (var index = 0; index < 1000; index++)
            {
                Assert.Empty(world.GetPlayersInRange(VectorD.Zero, 1));
                Assert.Equal(1024d * 1024, world.NearestPlayerDistanceSquared(VectorD.Zero));
            }
        }
        finally
        {
            racingStop.Cancel();
            await racingWriter;
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AGridLockDoesNotBlockOtherLevelsPositionsAndQueries()
    {
        await using var source = TestServerFactory.CreateServer($"player-grid-lock-{Guid.NewGuid():N}");
        await using var target = TestServerFactory.CreateServer($"player-grid-lock-target-{Guid.NewGuid():N}");
        var held = TestServerFactory.CreateConnectedPlayer(source.Server, source.World, "Held", VectorD.Zero);
        var moving = TestServerFactory.CreateConnectedPlayer(target.Server, target.World, "Moving", VectorD.Zero);
        Assert.True(source.World.TryAddPlayer(held));
        Assert.True(target.World.TryAddPlayer(moving));
        Snapshot(source.World);
        Snapshot(target.World);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (held.PlayerQueryState!.Grid!.Gate)
            {
                acquired.SetResult();
                release.Wait();
            }
        });
        Task? independent = null;
        try
        {
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            independent = Task.Run(() =>
            {
                moving.Position = new VectorD(1024, 7, 1024);
                Assert.Same(moving, Assert.Single(target.World.GetPlayersInRange(moving.Position, 1)));
            });
            await independent.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            await holder;
            if (independent is not null)
                await independent;
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
