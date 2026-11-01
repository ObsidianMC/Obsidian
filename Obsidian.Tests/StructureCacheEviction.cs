using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Configuration;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Commands;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Cache eviction must bound exploration memory without losing saved placement or search state.</summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(nameof(PluginTagFiles))]
public sealed class StructureCacheEviction
{
    private const int StartX = -320;
    private const int StartZ = -304;
    private const string Pyramid = "minecraft:desert_pyramid";

    [Fact]
    public async Task UnloadedStartsRestorePlacementStateAndKeepOpenedChestContents()
    {
        var generator = new MojangGenerator();
        var world = CreateWorld(generator);
        try
        {
            // Place only the start chunk of a pyramid spanning four chunks, and open one of its four chests.
            var chunk = (Chunk)await generator.GenerateChunkAsync(StartX, StartZ, stage: ChunkGenStage.features);
            var manager = generator.Builder!.Structures!;
            var cache = manager.StartCache;
            var original = manager.GetStarts(StartX, StartZ).Single(start => start.Structure.Identifier == Pyramid);
            var chests = chunk.GetBlockEntities().OfType<ContainerBlockEntity>()
                .Where(chest => chest.LootTable == "minecraft:chests/desert_pyramid").ToArray();
            Assert.Equal(4, chests.Length);
            var opened = chests[0];
            var chestPosition = opened.BlockPosition;
            opened.LootTable = null;
            opened.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 7);
            chunk.MarkUnsaved();
            var state = Snbt.Write(original.SaveState());
            _ = manager.FindReaching(StartX, StartZ);

            await UnloadRegions(world);
            Assert.Equal(0, cache.Count);

            // Finish the neighboring chunks with the same generator/cache, loading the start chunk from disk.
            await generator.GenerateChunkAsync(StartX + 1, StartZ, stage: ChunkGenStage.features);
            await generator.GenerateChunkAsync(StartX, StartZ + 1, stage: ChunkGenStage.features);
            await generator.GenerateChunkAsync(StartX + 1, StartZ + 1, stage: ChunkGenStage.features);
            var restored = manager.GetStarts(StartX, StartZ).Single(start => start.Structure.Identifier == Pyramid);
            Assert.NotSame(original, restored);
            Assert.True(restored.IsStateRestored);
            Assert.Equal(state, Snbt.Write(restored.SaveState()));
            Assert.Same(restored, manager.FindReaching(StartX, StartZ).Starts
                .Single(start => start.Structure.Identifier == Pyramid));

            var loaded = (Chunk)await world.GetChunkAsync(StartX, StartZ, scheduleGeneration: false);
            var kept = Assert.IsAssignableFrom<ContainerBlockEntity>(loaded.GetBlockEntity(
                chestPosition.X, chestPosition.Y, chestPosition.Z));
            Assert.Null(kept.LootTable);
            Assert.Same(ItemsRegistry.Diamond, kept.Inventory[0]!.Holder);
            Assert.Equal(7, kept.Inventory[0]!.Count);
            Assert.Equal(4, loaded.GetBlockEntities().OfType<ContainerBlockEntity>().Count());

            await UnloadRegions(world);
            Assert.Equal(0, cache.Count);
        }
        finally
        {
            await DisposeWorld(world);
        }
    }

    [Fact]
    public async Task ExploringAStripKeepsOnlyStartsWithinTheResidentRadius()
    {
        var generator = new MojangGenerator();
        var world = CreateWorld(generator);
        try
        {
            const int length = 96;
            const int firstX = 400;
            const int z = 400;
            for (var x = firstX; x < firstX + length; x++)
            {
                await generator.GenerateChunkAsync(x, z, stage: ChunkGenStage.carvers);
                if (x > firstX)
                    await world.LoadRegionByChunk(x - 1, z).UnloadChunk(x - 1, z);

                // One resident chunk can need only the 17 by 17 square of possible start chunks.
                Assert.InRange(generator.Builder!.Structures!.StartCache.Count, 1, 17 * 17);
            }

            await UnloadRegions(world);
            Assert.Equal(0, generator.Builder!.Structures!.StartCache.Count);
        }
        finally
        {
            await DisposeWorld(world);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangesAfterUnloadingSnapshotAreSavedBeforePinsAreReleased(
        bool wholeRegion, bool loadStartDuringUnload)
    {
        var generator = new MojangGenerator();
        var world = CreateWorld(generator);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? unloading = null;
        Task? changing = null;
        try
        {
            var residentX = loadStartDuringUnload ? StartX + 1 : StartX;
            var chunk = (Chunk)await world.GetChunkAsync(residentX, StartZ, scheduleGeneration: false);
            var manager = generator.Builder!.Structures!;
            var cache = manager.StartCache;
            var start = manager.GetStarts(StartX, StartZ).Single(candidate => candidate.Structure.Identifier == Pyramid);
            if (!loadStartDuringUnload)
                manager.LoadStart(start);

            var region = (Region)world.LoadRegionByChunk(StartX, StartZ);
            var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pauseNextWrite = 1;
            region.ChunkFile.Observe = step =>
            {
                if (step.Step == RegionFile.Step.PayloadWritten
                    && Interlocked.Exchange(ref pauseNextWrite, 0) == 1)
                {
                    paused.TrySetResult();
                    return new ValueTask(resume.Task);
                }

                return ValueTask.CompletedTask;
            };

            // The unloading snapshot precedes the reference. Its neighbor still needs the live start.
            using (cache.BeginGeneration(StartX + 8, StartZ))
            {
                unloading = wholeRegion
                    ? world.UnloadRegionAsync(region.X, region.Z)
                    : region.UnloadChunk(chunk.X, chunk.Z);
                await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

                // Exercise the real callbacks, including loading a previously absent start into a retiring region.
                changing = Task.Run(() =>
                {
                    manager.LoadStart(start);
                    Assert.True(start.TryAddReference());
                    manager.StartChanged!(StartX, StartZ);
                });
                await changing.WaitAsync(TimeSpan.FromSeconds(30));
                resume.TrySetResult();
                await unloading;
                Assert.Same(start, manager.GetStarts(StartX, StartZ).Single());
            }

            // No manual SaveStarts: the unload must persist the change and make eviction possible itself.
            await UnloadRegions(world);
            Assert.Equal(0, cache.Count);
            var saved = (Chunk)await world.GetChunkAsync(StartX, StartZ, scheduleGeneration: false);
            Assert.Equal(1, ((NbtCompound)saved.StructureStarts![Pyramid]).GetInt("references"));
        }
        finally
        {
            resume.TrySetResult();
            if (unloading is not null)
                await unloading;
            if (changing is not null)
                await changing;

            await DisposeWorld(world);
        }
    }

    [Fact]
    public async Task FailedRegionUnloadKeepsItsResidentStartsReachableForRetry()
    {
        var generator = new MojangGenerator();
        var world = CreateWorld(generator);
        Region? region = null;
        try
        {
            var chunk = (Chunk)await world.GetChunkAsync(StartX, StartZ, scheduleGeneration: false);
            var manager = generator.Builder!.Structures!;
            var cache = manager.StartCache;
            var start = manager.GetStarts(StartX, StartZ).Single(candidate => candidate.Structure.Identifier == Pyramid);
            manager.LoadStart(start);
            Assert.True(start.TryAddReference());
            manager.StartChanged!(StartX, StartZ);
            region = (Region)world.LoadRegionByChunk(StartX, StartZ);
            region.ChunkFile.Observe = step => step.Step == RegionFile.Step.PayloadWritten
                ? ValueTask.FromException(new IOException("Injected final-save failure."))
                : ValueTask.CompletedTask;

            await Assert.ThrowsAsync<IOException>(() => world.UnloadRegionAsync(region.X, region.Z));

            Assert.Same(region, Assert.Single(world.Regions.Values));
            Assert.Same(chunk, await world.GetChunkAsync(StartX, StartZ, scheduleGeneration: false));
            Assert.Same(start, manager.GetStarts(StartX, StartZ).Single());
            Assert.Equal(1, cache.Count);

            region.ChunkFile.Observe = null;
            await world.UnloadRegionAsync(region.X, region.Z);
            Assert.Empty(world.Regions);
            Assert.Equal(0, cache.Count);
            var saved = (Chunk)await world.GetChunkAsync(StartX, StartZ, scheduleGeneration: false);
            Assert.Equal(1, ((NbtCompound)saved.StructureStarts![Pyramid]).GetInt("references"));
        }
        finally
        {
            if (region is not null)
            {
                region.ChunkFile.Observe = null;
                if (!world.Regions.Values.Contains(region))
                    await region.DisposeAsync();
            }

            await DisposeWorld(world);
        }
    }

    [Fact]
    public void SavedStartsCanBeEvictedAfterTheirStructureSetIsRemoved()
    {
        var source = PluginStructures.CurrentRegistry;
        var pipeline = NativeChunks.Pipeline(12345L, () => source);
        var manager = pipeline.Structures!;
        var cache = manager.StartCache;
        var startChunk = new Chunk(StartX, StartZ);
        var neighbor = new Chunk(StartX + 8, StartZ);
        cache.Track(startChunk);
        cache.Track(neighbor);
        var start = manager.GetStarts(StartX, StartZ).Single(candidate => candidate.Structure.Identifier == Pyramid);
        StructureManager.RestoreStart(start, null);
        Assert.True(start.TryAddReference());
        var saved = manager.SaveStarts(StartX, StartZ, null)!;
        cache.Untrack(startChunk);

        // Pruning retains the already-saved state without keeping its pieces or requiring an absent chunk to save again.
        source = source with { Version = source.Version + 1, Sets = new Dictionary<string, StructureSet>() };
        manager.Refresh();
        Assert.Empty(manager.GetStarts(StartX, StartZ));
        cache.Untrack(neighbor);
        Assert.Equal(0, cache.Count);

        source = PluginStructures.CurrentRegistry with { Version = source.Version + 1 };
        manager.Refresh();
        cache.Track(startChunk);
        var again = manager.GetStarts(StartX, StartZ).Single(candidate => candidate.Structure.Identifier == Pyramid);
        StructureManager.RestoreStart(again, saved);
        Assert.NotSame(start, again);
        Assert.Equal(1, again.References);
        _ = manager.SaveStarts(StartX, StartZ, saved);
        cache.Untrack(startChunk);
        Assert.Equal(0, cache.Count);
    }

    private static World CreateWorld(MojangGenerator generator)
    {
        var configuration = new ServerConfiguration { SpawnChunkRadius = 0, TimeTickSpeedMultiplier = 1 };
        var world = new World(
            NullLogger<World>.Instance,
            new FakeWorldManager(defaultWorld: null!),
            new FakePacketBroadcaster(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new FakeEventDispatcher(),
            generator,
            $"structure-eviction-{Guid.NewGuid():N}",
            "12345");
        world.Initialize(new DimensionCodec
        {
            Name = "minecraft:overworld",
            Id = 0,
            Element = new DimensionElement
            {
                MonsterSpawnBlockLightLimit = 0,
                MonsterSpawnLightLevel = new MonsterSpawnLightLevel { IntValue = 0 },
                HasSkylight = true,
                MinY = -64,
                Height = 384,
                LogicalHeight = 384,
                CoordinateScale = 1
            }
        });
        return world;
    }

    private static async Task UnloadRegions(World world)
    {
        foreach (var region in world.Regions.Values.ToArray())
            await world.UnloadRegionAsync(region.X, region.Z);
    }

    private static async Task DisposeWorld(World world)
    {
        await world.DisposeAsync();
        Directory.Delete(world.WorldFolderPath, recursive: true);
        PluginDimensions.GiveBack("minecraft:overworld");
    }
}
