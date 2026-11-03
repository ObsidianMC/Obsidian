using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Maps;
using Obsidian.API.Loot;
using Obsidian.API.Inventory.DataComponents;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Pregeneration stages the Mojang generator's area a tile at a time, saving each tile and unloading the chunks no
/// tile left needs.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public sealed class Pregeneration
{
    // Generating the area takes most of TestServerFactory.TestTimeout in a debug build on its own, so the parallel
    // worldgen replays could push it past that; the timeout only has to catch a hang.
    private const int GenerationTimeout = 4 * TestServerFactory.TestTimeout;

    [Fact(Timeout = GenerationTimeout)]
    public async Task PregeneratedChunksAreSavedAndUnloaded()
    {
        const int range = 2;
        // A fresh registry exercises placement-time cache initialization in the parallel waves.
        var features = new Obsidian.WorldData.Generators.Features.FeatureRegistry(
            Obsidian.WorldData.Generators.Density.WorldgenData.Vanilla);
        var structures = Obsidian.WorldData.Generators.Features.StructureData.Read(features);
        var configuration = new ServerConfiguration { SpawnChunkRadius = 1, TimeTickSpeedMultiplier = 1, PregenerateChunkRange = range };
        var world = new World(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<World>.Instance,
            new FakeWorldManager(defaultWorld: null!),
            new FakePacketBroadcaster(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new FakeEventDispatcher(),
            new MojangGenerator { Features = () => features, Structures = () => structures },
            $"pregeneration-{Guid.NewGuid():N}",
            "12345");
        world.Initialize(new DimensionCodec
        {
            Name = "minecraft:overworld",
            Id = 0,
            Element = new DimensionElement
            {
                MonsterSpawnBlockLightLimit = 0,
                MonsterSpawnLightLevel = new MonsterSpawnLightLevel { IntValue = 0 },
                AmbientLight = 0,
                HasSkylight = true,
                MinY = -64,
                Height = 384,
                LogicalHeight = 384,
                CoordinateScale = 1,
                HasCeiling = false
            }
        });

        try
        {
            await world.GenerateAsync();

            // Done with every tile, only the spawn chunks stay loaded, and every chunk of the area comes back complete
            // from disk.
            var (centerX, centerZ) = world.LevelData.SpawnPosition.ToChunkCoord();
            long[] spawnChunks =
            [
                NumericsHelper.IntsToLong(centerX - 1, centerZ - 1), NumericsHelper.IntsToLong(centerX - 1, centerZ),
                NumericsHelper.IntsToLong(centerX, centerZ - 1), NumericsHelper.IntsToLong(centerX, centerZ)
            ];
            Assert.Equal(spawnChunks.Order(), world.LoadedChunks.Order());
            for (var x = centerX - range; x < centerX + range; x++)
            {
                for (var z = centerZ - range; z < centerZ + range; z++)
                    Assert.True((await world.GetChunkAsync(x, z, scheduleGeneration: false))!.IsGenerated);
            }

            // Searches and explorer previews use the running generator's current structure and biome paths.
            var generator = Assert.IsType<MojangGenerator>(world.Generator);
            var found = generator.LocateStructure(locator => locator.Locate(
                "minecraft:village", (Vector)world.LevelData.SpawnPosition.Floor(), 100, false));
            Assert.NotNull(found);
            var maps = Assert.IsType<ExplorationMaps>(ExplorationMaps.For(world));
            var map = maps.Create(new ExplorationMapRequest("minecraft:village", world.LevelData.SpawnPosition,
                100, false, 1, "minecraft:mansion"));
            Assert.NotNull(map);
            Assert.NotNull(MapStorage.GetMapId(map));
            Assert.NotNull(map[DataComponentType.MapDecorations]);
        }
        finally
        {
            await world.DisposeAsync();
            Directory.Delete(world.WorldFolderPath, recursive: true);

            // The world's generator took the overworld's stem, which data pack startup tests expect free.
            Obsidian.Registries.PluginDimensions.GiveBack("minecraft:overworld");
        }
    }

    // The 16 chunks staging generates in the scheduler tests; decorating the one at (1, 1) writes into every chunk
    // whose post-processing reads it, so no post-processing or lighting can start before it's done.
    private static readonly (int X, int Z)[] StagedChunks =
        [.. Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4).Select(z => (x, z)))];

    private const int StagingWorkers = 4;

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task StagingStartsEachStepOnlyAfterTheStepsItMustFollow()
    {
        var done = new ConcurrentDictionary<(StagingStep Step, int X, int Z), bool>();
        var missingAtStart = new ConcurrentBag<((StagingStep, int, int) Step, (StagingStep, int, int) Needed)>();
        var graph = StagingGraph.For(StagedChunks, async (step, position) =>
        {
            foreach (var needed in MustFollow(step, position))
            {
                if (!done.ContainsKey(needed))
                    missingAtStart.Add(((step, position.X, position.Z), needed));
            }

            // Lets other workers start steps while this one is running.
            await Task.Yield();
            done[(step, position.X, position.Z)] = true;
        });

        await graph.RunAsync(StagingWorkers);

        // Every step ran: 8 by 8 carved, 6 by 6 decorated, and the 16 chunks post-processed and lit. A step needed
        // later only counts if the graph has it, which it then ran.
        Assert.Equal(64 + 36 + 16 + 16, done.Count);
        Assert.DoesNotContain(missingAtStart, entry => done.ContainsKey(entry.Needed));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task StagingStopsAtAFailingStepAndRethrowsIt()
    {
        var failure = new InvalidOperationException("decorating failed");
        var ran = new ConcurrentBag<StagingStep>();
        var graph = StagingGraph.For(StagedChunks, async (step, position) =>
        {
            await Task.Yield();
            if (step == StagingStep.Decorate && position == (1, 1))
                throw failure;

            ran.Add(step);
        });

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => graph.RunAsync(StagingWorkers));

        Assert.Same(failure, thrown);
        Assert.DoesNotContain(StagingStep.Finish, ran);
        Assert.DoesNotContain(StagingStep.Light, ran);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task CancellingStagingStopsItsLaterSteps()
    {
        using var cancellation = new CancellationTokenSource();
        var ran = new ConcurrentBag<StagingStep>();
        var graph = StagingGraph.For(StagedChunks, async (step, position) =>
        {
            await Task.Yield();
            if (step == StagingStep.Decorate && position == (1, 1))
                cancellation.Cancel();

            ran.Add(step);
        }, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => graph.RunAsync(StagingWorkers));

        Assert.DoesNotContain(StagingStep.Finish, ran);
        Assert.DoesNotContain(StagingStep.Light, ran);
    }

    [Fact]
    public void GenerationThreadsCountEveryAllowedProcessor()
    {
        // A Linux process allowed CPUs 63 to 70 of a large host, one per core: every one of them counts, though
        // Process.ProcessorAffinity only shows CPU 63.
        var allowed = PhysicalCores.ParseCpuList("63-70")!;
        var cores = PhysicalCores.CountCores(allowed, processorCount: 8, cpu => cpu.ToString());
        Assert.Equal(8, cores);
        Assert.Equal(7, GenerationThreads.ThreadCount(8, cores));

        // Two cores of two hardware threads each: one thread per core.
        var smt = PhysicalCores.CountCores(PhysicalCores.ParseCpuList("0-3")!, processorCount: 4, cpu => $"{cpu & ~1}");
        Assert.Equal(2, GenerationThreads.ThreadCount(4, smt));

        // Fewer processors seen than the runtime counts: the logical count holds.
        Assert.Null(PhysicalCores.CountCores([63], processorCount: 8, cpu => cpu.ToString()));
        Assert.Equal(7, GenerationThreads.ThreadCount(8, null));
    }

    // The steps a step must follow in staging: decorating follows its area's carving, and post-processing and
    // lighting follow the steps of the kind before them within 2 chunks. Steps of one kind within 2 chunks of each
    // other follow those of lower colour (a 3 by 3 colouring of positions), the order of running each kind in 9 waves.
    private static IEnumerable<(StagingStep Step, int X, int Z)> MustFollow(StagingStep step, (int X, int Z) position)
    {
        if (step == StagingStep.Carve)
            yield break;

        var reach = step == StagingStep.Decorate ? 1 : 2;
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
            {
                var (x, z) = (position.X + dx, position.Z + dz);
                if (Math.Abs(dx) <= reach && Math.Abs(dz) <= reach)
                    yield return (step - 1, x, z);

                if (Colour(x, z) < Colour(position.X, position.Z))
                    yield return (step, x, z);
            }
        }
    }

    private static int Colour(int x, int z) => Mod3(x) * 3 + Mod3(z);

    private static int Mod3(int value) => (value % 3 + 3) % 3;
}
