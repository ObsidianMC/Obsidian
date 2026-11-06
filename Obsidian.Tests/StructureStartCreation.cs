#nullable enable

using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Creation coordinates concurrent callers without holding plugin code or cache lifetimes hostage.</summary>
[Trait("Category", TestCategory.Worldgen)]
public sealed class StructureStartCreation
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentRequestsCreateOneStartAndRecreateItAfterEviction()
    {
        var cache = new StructureStartCache();
        var layout = Layout();
        var resident = new Chunk(0, 0);
        cache.Track(resident);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var creations = 0;

        StructureStart Create(StructureLayout _, StructureSet set, int x, int z, out bool refused)
        {
            refused = false;
            Interlocked.Increment(ref creations);
            entered.Set();
            Assert.True(release.Wait(Timeout));
            return Start(set, x, z);
        }

        var owner = Request(() => cache.Get(0, 0, layout, Create));
        var waiters = new List<(Thread Thread, Task<StructureStartCache.ChunkStarts?> Result)>();
        try
        {
            Assert.True(entered.Wait(Timeout));
            for (var i = 0; i < 3; i++)
            {
                waiters.Add(Request(() => cache.Get(0, 0, layout, Create)));
            }

            WaitUntilBlocked(waiters.Select(waiter => waiter.Thread));
        }
        finally
        {
            release.Set();
        }

        var results = await Task.WhenAll(waiters.Select(waiter => waiter.Result).Prepend(owner.Result))
            .WaitAsync(Timeout);
        var created = Assert.Single(results[0]!.Starts);
        Assert.All(results, result => Assert.Same(created, Assert.Single(result!.Starts)));
        Assert.Equal(1, creations);

        // Completed coordination must not keep the evicted entry or return its retired start on a later request.
        cache.Untrack(resident);
        Assert.Equal(0, cache.Count);
        var recreated = Assert.Single(cache.Get(0, 0, layout, Create)!.Starts);
        Assert.NotSame(created, recreated);
        Assert.Equal(2, creations);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FailedCreationReleasesEveryWaiterAndAllowsRetry()
    {
        var cache = new StructureStartCache();
        var layout = Layout();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failure = new InvalidOperationException("Injected structure creation failure.");
        var failing = true;
        var attempts = 0;

        StructureStart Create(StructureLayout _, StructureSet set, int x, int z, out bool refused)
        {
            refused = false;
            Interlocked.Increment(ref attempts);
            if (failing)
            {
                entered.Set();
                Assert.True(release.Wait(Timeout));
                throw failure;
            }

            return Start(set, x, z);
        }

        var owner = Request(() => cache.Get(0, 0, layout, Create));
        var waiters = new List<(Thread Thread, Task<StructureStartCache.ChunkStarts?> Result)>();
        try
        {
            Assert.True(entered.Wait(Timeout));
            for (var i = 0; i < 3; i++)
            {
                waiters.Add(Request(() => cache.Get(0, 0, layout, Create)));
            }

            WaitUntilBlocked(waiters.Select(waiter => waiter.Thread));
        }
        finally
        {
            release.Set();
        }

        var errors = await Task.WhenAll(waiters.Select(waiter => waiter.Result).Prepend(owner.Result)
            .Select(result => Record.ExceptionAsync(async () => await result))).WaitAsync(Timeout);
        Assert.All(errors, error => Assert.Same(failure, error));
        Assert.Equal(1, attempts);

        failing = false;
        var retried = Assert.Single(cache.Get(0, 0, layout, Create)!.Starts);
        Assert.Same(retried, Assert.Single(cache.Get(0, 0, layout, Create)!.Starts));
        Assert.Equal(2, attempts);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReentrantCreationCanPublishItsOwnKey()
    {
        var cache = new StructureStartCache();
        var layout = Layout();
        var first = true;
        StructureStart? nested = null;

        StructureStart Create(StructureLayout _, StructureSet set, int x, int z, out bool refused)
        {
            refused = false;
            if (first)
            {
                first = false;
                nested = Assert.Single(cache.Get(x, z, layout, Create)!.Starts);
            }

            return Start(set, x, z);
        }

        var request = Request(() => cache.Get(0, 0, layout, Create));
        var result = await request.Result.WaitAsync(Timeout);

        Assert.NotNull(nested);
        Assert.Same(nested, Assert.Single(result!.Starts));
        Assert.Same(nested, Assert.Single(cache.Get(0, 0, layout, Create)!.Starts));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task NestedRequestsForEachOthersKeysDoNotWaitOnTheOtherCreator()
    {
        var cache = new StructureStartCache();
        var layout = Layout();
        using var entered = new Barrier(2);
        using var nested = new ThreadLocal<bool>();

        StructureStart Create(StructureLayout _, StructureSet set, int x, int z, out bool refused)
        {
            refused = false;
            if (!nested.Value)
            {
                nested.Value = true;
                try
                {
                    Assert.True(entered.SignalAndWait(Timeout));
                    Assert.NotNull(cache.Get(1 - x, z, layout, Create));
                }
                finally
                {
                    nested.Value = false;
                }
            }

            return Start(set, x, z);
        }

        var left = Request(() => cache.Get(0, 0, layout, Create));
        var right = Request(() => cache.Get(1, 0, layout, Create));
        var results = await Task.WhenAll(left.Result, right.Result).WaitAsync(Timeout);

        for (var x = 0; x < results.Length; x++)
        {
            var start = Assert.Single(results[x]!.Starts);
            Assert.Equal(x, start.ChunkX);
            Assert.Same(start, Assert.Single(cache.Get(x, 0, layout, Create)!.Starts));
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RefreshDuringCreationRejectsOldStartsWithoutBlockingTheNewLayout()
    {
        var cache = new StructureStartCache();
        var oldLayout = Layout();
        var newLayout = Layout(version: 2);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        StructureStart Create(StructureLayout layout, StructureSet set, int x, int z, out bool refused)
        {
            refused = false;
            if (layout == oldLayout)
            {
                entered.Set();
                Assert.True(release.Wait(Timeout));
            }

            return Start(set, x, z);
        }

        var stale = Request(() => cache.Get(0, 0, oldLayout, Create));
        StructureStart? current = null;
        try
        {
            Assert.True(entered.Wait(Timeout));
            cache.Prune(newLayout.Source);
            var fresh = Request(() => cache.Get(0, 0, newLayout, Create));
            current = Assert.Single((await fresh.Result.WaitAsync(Timeout))!.Starts);
            Assert.Same(newLayout.Sets[0].Structures[0].Structure, current.Structure);
        }
        finally
        {
            release.Set();
        }

        Assert.Null(await stale.Result.WaitAsync(Timeout));
        Assert.Same(current, Assert.Single(cache.Get(0, 0, newLayout, Create)!.Starts));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task NewerPublicationWinsWhenTheOldCreatorFinishesWithoutAPrune()
    {
        var cache = new StructureStartCache();
        var oldLayout = Layout();
        var newLayout = Layout(version: 2);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        StructureStart Create(StructureLayout layout, StructureSet set, int x, int z, out bool refused)
        {
            refused = false;
            if (layout == oldLayout)
            {
                entered.Set();
                Assert.True(release.Wait(Timeout));
            }

            return Start(set, x, z);
        }

        var stale = Request(() => cache.Get(0, 0, oldLayout, Create));
        StructureStart? current = null;
        try
        {
            Assert.True(entered.Wait(Timeout));
            var fresh = Request(() => cache.Get(0, 0, newLayout, Create));
            current = Assert.Single((await fresh.Result.WaitAsync(Timeout))!.Starts);
            Assert.Same(newLayout.Sets[0].Structures[0].Structure, current.Structure);
        }
        finally
        {
            release.Set();
        }

        var resumed = await stale.Result.WaitAsync(Timeout);
        Assert.Same(current, Assert.Single(resumed!.Starts));
        Assert.Equal(newLayout.Version, resumed.Version);
    }

    private static StructureLayout Layout(long version = 1)
    {
        var structure = new ProbeStructure
        {
            Identifier = "test:creation",
            Biomes = new BiomeSet("minecraft:plains")
        };
        var set = new StructureSet
        {
            Identifier = "test:creation",
            Structures = [new StructureSetEntry { Structure = structure, Weight = 1 }],
            Placement = new RandomSpreadStructurePlacement { Spacing = 1, Separation = 0 }
        };
        var source = WorldgenStructures.Empty with
        {
            Version = version,
            Structures = new Dictionary<string, Structure> { [structure.Identifier] = structure },
            Sets = new Dictionary<string, StructureSet> { [set.Identifier] = set }
        };

        return new StructureLayout(source, [CodecRegistry.Biomes.Plains]);
    }

    private static StructureStart Start(StructureSet set, int x, int z) =>
        new(set.Structures[0].Structure, x, z, [new ProbePiece(x, z)]);

    private static (Thread Thread, Task<T> Result) Request<T>(Func<T> run)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(run());
            }
            catch (Exception exception)
            {
                result.SetException(exception);
            }
        })
        {
            IsBackground = true
        };
        thread.Start();

        return (thread, result.Task);
    }

    private static void WaitUntilBlocked(IEnumerable<Thread> threads)
    {
        // The creator remains blocked until every requester has either joined it or entered a duplicate factory.
        // This makes the one-creation assertion distinguish coordination from a lucky cache hit after publication.
        var waiting = threads.ToArray();
        Assert.True(SpinWait.SpinUntil(
            () => waiting.All(thread => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0), Timeout));
    }

    private sealed class ProbeStructure : Structure
    {
        internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) => null;
    }

    private sealed class ProbePiece(int x, int z)
        : StructurePiece(0, BlockBox.Create(x << 4, 64, z << 4, (x << 4) + 15, 64, (z << 4) + 15))
    {
        internal override string TypeId => "test:creation";

        public override void PostProcess(StructurePieceContext context)
        {
        }
    }
}
