using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockTicks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Saves write only the chunks that changed since their last save: every change a chunk saves marks it, and a region
/// flush skips the chunks no change marked.
/// </summary>
public sealed class ChunkSaves
{
    private static readonly Vector Spawner = new(4, 30, 4);
    private static readonly Vector Crate = new(6, 30, 6);

    // Each change a chunk saves, made after an optional setup the chunk was saved with.
    private static readonly Dictionary<string, (Action<Chunk>? Setup, Action<Chunk> Change)> changes = new()
    {
        ["block"] = (null, chunk => chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone)),
        ["generation write"] = (null, chunk => ((ChunkSection)chunk.Sections[8]).SetStateIdUntracked(1, 2, 3, BlocksRegistry.Stone.GetHashCode())),
        ["container write"] = (null, chunk => chunk.Sections[8].BlockStateContainer.Set(1, 2, 3, BlocksRegistry.Stone)),
        ["biome"] = (null, chunk => chunk.SetBiome(0, 64, 0, CodecRegistry.Biomes.Desert)),
        ["heightmap"] = (null, chunk => chunk.Heightmaps[HeightmapType.MotionBlocking].Set(1, 1, 70)),
        ["heightmap removed"] = (null, chunk => chunk.Heightmaps.Remove(HeightmapType.WorldSurfaceWG)),
        ["light"] = (null, chunk => chunk.SetLightLevel(1, 64, 1, LightType.Block, 7)),
        ["block entity added"] = (null, chunk => chunk.SetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z, CreateSpawner())),
        ["block entity removed"] = (AddSpawner, chunk => chunk.RemoveBlockEntity(Spawner.X, Spawner.Y, Spawner.Z)),
        ["block entities cleared"] = (AddCrate, chunk => chunk.BlockEntities.Clear()),
        // A spawner's data changes without telling its chunk (its delay counting down, say).
        ["block entity data"] = (AddSpawner, chunk => ((DataBlockEntity)chunk.GetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z)).Data.Add(new NbtTag<short>("Delay", 20))),
        // A container's items, which players move without telling its chunk.
        ["container items"] = (AddCrate, chunk => ((ContainerBlockEntity)chunk.GetBlockEntity(Crate.X, Crate.Y, Crate.Z)).Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 1))),
        ["block tick"] = (null, chunk => chunk.BlockTicks.Schedule(new Vector(1, 64, 1), "minecraft:stone", 5)),
        ["fluid tick"] = (null, chunk => chunk.FluidTicks.Schedule(new Vector(1, 64, 1), FluidKind.Water, 5)),
        ["status"] = (null, chunk => chunk.SetChunkStatusCore(ChunkGenStage.full)),
        ["structure starts"] = (null, chunk => chunk.StructureStarts = new NbtCompound("starts")),
    };

    // A change that left its chunk saved would be lost at the next flush.
    [Fact]
    public void ChangesMarkTheChunkUnsaved()
    {
        FixtureCases.AssertAll(changes.Keys, change => change, AssertChangeMarksTheChunkUnsaved);
    }

    private static void AssertChangeMarksTheChunkUnsaved(string change)
    {
        var (setup, apply) = changes[change];
        var chunk = new Chunk(0, 0, ChunkGenStage.light);
        setup?.Invoke(chunk);

        // A new chunk is unsaved; once a save took it, it isn't until it changes, and only once per change.
        Assert.True(chunk.TakeUnsaved());
        Assert.False(chunk.TakeUnsaved());

        apply(chunk);
        Assert.True(chunk.TakeUnsaved());
        Assert.False(chunk.TakeUnsaved());
    }

    [Fact(DisplayName = "Inhabited time doesn't mark a chunk unsaved, like vanilla's")]
    public void InhabitedTimeLeavesTheChunkSaved()
    {
        var chunk = new Chunk(0, 0, ChunkGenStage.light);
        Assert.True(chunk.TakeUnsaved());

        chunk.InhabitedTime++;
        Assert.False(chunk.TakeUnsaved());
    }

    [Fact]
    public async Task FlushWritesOnlyChangedChunks()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-chunk-saves-{Guid.NewGuid():N}");
        try
        {
            // The region asks for the starts of each chunk it writes, and only those.
            var written = new List<(int X, int Z)>();
            var region = new Region(0, 0, folder)
            {
                SaveStructureStarts = (x, z, loaded) =>
                {
                    written.Add((x, z));
                    return loaded;
                },
            };

            await using (region)
            {
                await region.InitAsync();
                var changed = Complete(0, 0);
                region.SetChunk(changed);
                region.SetChunk(Complete(1, 0));

                await region.FlushAsync();
                Assert.Equal([(0, 0), (1, 0)], written.Order());

                written.Clear();
                await region.FlushAsync();
                Assert.Empty(written);

                changed.SetBlock(2, 70, 3, BlocksRegistry.Stone);
                await region.FlushAsync();
                Assert.Equal([(0, 0)], written);
            }

            await using (var reloaded = new Region(0, 0, folder))
            {
                await reloaded.InitAsync();
                var reloadedChunk = await reloaded.GetChunkAsync(0, 0);
                Assert.Equal(BlockIds.Stone, reloadedChunk.GetBlock(2, 70, 3).RegistryId);
                Assert.NotNull(await reloaded.GetChunkAsync(1, 0));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // Pregeneration unloads the chunks it just saved before the level first ticks: their ticks, saved with delays
    // relative to the game time, are on disk as they are until the game time moves.
    [Fact]
    public async Task UnloadWritesOnlyChunksChangedSinceTheirSave()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-chunk-saves-{Guid.NewGuid():N}");
        try
        {
            var written = new List<(int X, int Z)>();
            var scheduler = new BlockTickScheduler();
            var region = new Region(0, 0, folder)
            {
                SaveStructureStarts = (x, z, loaded) =>
                {
                    written.Add((x, z));
                    return loaded;
                },

                // Like the level's, which stops an unloading chunk's ticks as its final save starts.
                BlockAccessUnloaded = chunk => ((Chunk)chunk).BlockTicks.StopTicking(),
            };

            await using (region)
            {
                await region.InitAsync();
                var chunks = Enumerable.Range(0, 3).Select(x => Ticking(x, scheduler)).ToArray();
                foreach (var chunk in chunks)
                    region.SetChunk(chunk);

                await region.FlushAsync();
                written.Clear();

                // Unloaded together, only the chunk changed since is written again.
                chunks[0].SetBlock(2, 70, 3, BlocksRegistry.Stone);
                await region.UnloadChunksAsync([(0, 0), (1, 0)]);
                Assert.Equal([(0, 0)], written);

                // Once the game time moves, the saved delays are a tick too long.
                written.Clear();
                scheduler.Tick(_ => false, _ => { });
                await region.UnloadChunksAsync([(2, 0)]);
                Assert.Equal([(2, 0)], written);
                Assert.Empty(region.ResidentChunks());
            }

            await using (var reloaded = new Region(0, 0, folder))
            {
                await reloaded.InitAsync();
                var changed = await reloaded.GetChunkAsync(0, 0);
                Assert.Equal(BlockIds.Stone, changed.GetBlock(2, 70, 3).RegistryId);

                var late = (Chunk)await reloaded.GetChunkAsync(2, 0);
                Assert.Equal(4, Assert.Single(late.BlockTicks.Pack()).Delay);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task EntitiesThatLeftAreSavedAsGone()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-chunk-saves-{Guid.NewGuid():N}");
        try
        {
            await using (var region = new Region(0, 0, folder))
            {
                await region.InitAsync();
                var chunk = Complete(0, 0);
                region.SetChunk(chunk);
                var item = new ItemEntity
                {
                    Level = null!,
                    EntityId = 1,
                    Position = new VectorF(8.5f, 65f, 8.5f),
                    Item = new ItemStack(ItemsRegistry.Apple, 3)
                };
                region.TryAddEntity(item);

                await region.FlushAsync();
                Assert.Equal(1, chunk.SavedEntityCount);

                // The chunk itself is unchanged, but its entity file still lists the item.
                region.TryRemoveEntity(item);
                await region.FlushAsync();
            }

            var loaded = new List<Chunk>();
            await using (var reloaded = new Region(0, 0, folder) { EntitiesLoaded = loaded.Add })
            {
                await reloaded.InitAsync();
                var chunk = (Chunk)await reloaded.GetChunkAsync(0, 0);
                Assert.Empty(chunk.PendingEntities);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact(DisplayName = "An autosave lets the level tick while it writes, and keeps changes made meanwhile")]
    public async Task LevelTicksWhileTheSaveWrites()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-tick-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = Complete(0, 0);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.SetChunk(chunk);
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);

        // The save's disk flush waits until released, after its snapshots are taken.
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != RegionFile.Step.Flushed)
                return;

            writing.TrySetResult();
            await release.Task;
        };

        var flush = world.FlushRegionsAsync();
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // While it writes, the level ticks, and a change made then is left for the next save.
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(10));
            chunk.SetBlock(2, 64, 2, BlocksRegistry.Stone);
        }
        finally
        {
            // Released whatever happened, so a failure fails the test instead of hanging its disposal.
            release.TrySetResult();
        }

        await flush.WaitAsync(TimeSpan.FromSeconds(10));
        region.ChunkFile.Observe = null;

        Assert.True(chunk.IsUnsaved);
    }

    [Fact(DisplayName = "A chunk unloaded while an autosave writes keeps a change that save failed to write")]
    public async Task UnloadDuringAFailingAutosaveKeepsTheChange()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = Complete(0, 0);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.SetChunk(chunk);
        await region.FlushAsync();
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);

        // The autosave's disk flush waits until released, then fails.
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = 1;
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != RegionFile.Step.Flushed || Interlocked.Exchange(ref failing, 0) == 0)
                return;

            writing.TrySetResult();
            await release.Task;
            throw new IOException("The disk is full.");
        };

        var flush = world.FlushRegionsAsync();
        var unload = Task.CompletedTask;
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // While it writes, the chunk changes again and unloads. The unload waits for the autosave to end before
            // it takes its snapshot, which would take the chunk's unsaved marks.
            chunk.SetBlock(2, 64, 2, BlocksRegistry.Stone);
            unload = region.UnloadChunk(0, 0);
            var snapshotTaken = SpinWait.SpinUntil(() => !chunk.IsUnsaved, TimeSpan.FromMilliseconds(500));
            Assert.False(snapshotTaken);
        }
        finally
        {
            // Released whatever happened, so a failure fails the test instead of hanging its disposal.
            release.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => flush.WaitAsync(TimeSpan.FromSeconds(10)));
        await unload.WaitAsync(TimeSpan.FromSeconds(10));
        region.ChunkFile.Observe = null;

        // The unload wrote both changes, the one the autosave couldn't included.
        Assert.Null(region.GetLoadedChunk(0, 0));
        var reloaded = await region.GetChunkAsync(0, 0);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(1, 64, 1).RegistryId);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(2, 64, 2).RegistryId);
    }

    [Fact(DisplayName = "A tick saves a changed chunk ahead of the autosave, once per cooldown")]
    public async Task TicksSaveChangedChunksEagerly()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-eager-{Guid.NewGuid():N}");
        var world = context.World;

        // However long the tick takes on a slow machine, it saves.
        world.EagerSaveTickBudget = TimeSpan.MaxValue;
        var chunk = Complete(0, 0);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.SetChunk(chunk);
        await region.FlushAsync();

        // The tick takes the change's snapshot, and writes it while the next ticks run. A tick leaves the chunk for a
        // later one while every staging slot is busy (other tests save too), or generation holds the chunk's lock.
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            await world.DoWorldTickAsync();
        }
        while (chunk.IsUnsaved && DateTime.UtcNow < deadline);

        Assert.False(chunk.IsUnsaved);

        // A second change within the cooldown waits for a later save.
        chunk.SetBlock(2, 64, 2, BlocksRegistry.Stone);
        await world.DoWorldTickAsync();
        Assert.True(chunk.IsUnsaved);

        // Undone before the unload, with the light it changes settled first, so the unload has nothing of the blocks
        // to write: what's on disk is the tick's save.
        chunk.SetBlock(2, 64, 2, BlocksRegistry.Air);
        world.Lighting.Flush();
        chunk.TakeUnsaved();
        await region.UnloadChunk(0, 0);

        var reloaded = await region.GetChunkAsync(0, 0);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(1, 64, 1).RegistryId);
    }

    [Fact(DisplayName = "An eager save of a newly complete chunk keeps the entities its proto record held")]
    public async Task EagerSaveKeepsANewlyCompleteChunksEntities()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-chunk-saves-{Guid.NewGuid():N}");
        try
        {
            await using (var region = new Region(0, 0, folder))
            {
                await region.InitAsync();

                // Saved while generating, with an entity generation placed: the proto record holds it.
                var chunk = new Chunk(0, 0);
                chunk.SetChunkStatus(ChunkGenStage.features);
                chunk.PendingEntities.Add(new GeneratedEntity("minecraft:end_crystal", new VectorD(8.5, 80, 8.5)));
                region.SetChunk(chunk);
                await region.FlushAsync();

                // Complete and changed, it's saved eagerly; then the server stops before anything else is saved.
                chunk.SetChunkStatus(ChunkGenStage.full);
                chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
                await SaveEagerlyAsync(region, chunk);
            }

            await using (var reloaded = new Region(0, 0, folder))
            {
                await reloaded.InitAsync();
                var chunk = (Chunk)await reloaded.GetChunkAsync(0, 0);

                Assert.True(chunk.IsGenerated);
                Assert.Single(chunk.PendingEntities);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact(DisplayName = "An autosave lets the level tick while an eager save writes")]
    public async Task AutosaveDoesNotHoldTheTickForAnEagerSave()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-eager-autosave-{Guid.NewGuid():N}");
        var world = context.World;
        world.EagerSaveTickBudget = TimeSpan.MaxValue;
        var chunk = Complete(0, 0);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.SetChunk(chunk);
        await region.FlushAsync();

        // An eager save's disk flush waits until released, holding the region's flush lock.
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != RegionFile.Step.Flushed)
                return;

            writing.TrySetResult();
            await release.Task;
        };

        Task flush = Task.CompletedTask;
        try
        {
            // A tick saves the change eagerly (a later one, while the staging slots are busy).
            chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            do
            {
                await world.DoWorldTickAsync();
            }
            while (!writing.Task.IsCompleted && DateTime.UtcNow < deadline);

            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // An autosave starts meanwhile; the level still ticks.
            chunk.SetBlock(2, 64, 2, BlocksRegistry.Stone);
            flush = world.FlushRegionsAsync();
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Released whatever happened, so a failure fails the test instead of hanging its disposal.
            release.TrySetResult();
        }

        await flush.WaitAsync(TimeSpan.FromSeconds(10));
        region.ChunkFile.Observe = null;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LevelTicksWhileAnUnloadWritesAndAutosaveWaits(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-tick-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.Flushed);

        var unload = StartUnload(world, region, wholeRegion);
        Task autosave = Task.CompletedTask;
        try
        {
            await writing.WaitAsync(TimeSpan.FromSeconds(10));
            autosave = world.FlushRegionsAsync();

            var before = world.LevelData.GameTime;
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before + 1, world.LevelData.GameTime);
            Assert.False(unload.IsCompleted);
            Assert.False(autosave.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(unload, autosave).WaitAsync(TimeSpan.FromSeconds(10));
            region.ChunkFile.Observe = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReloadDuringAnUnloadKeepsTheLatestBlocksAndBlockEntities(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-reload-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        await region.FlushAsync();

        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        AddSpawner(chunk);
        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.PayloadWritten);
        var unload = StartUnload(world, region, wholeRegion);
        try
        {
            await writing.WaitAsync(TimeSpan.FromSeconds(10));

            // The resident instance remains readable until its final write commits; the disk still has the old data.
            var duringWrite = await world.GetChunkAsync(64, 64, scheduleGeneration: false);
            Assert.NotNull(duringWrite);
            Assert.Equal(BlockIds.Stone, duringWrite.GetBlock(1, 64, 1).RegistryId);
            Assert.NotNull(duringWrite.GetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z));
            Assert.False(unload.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await unload.WaitAsync(TimeSpan.FromSeconds(10));
            region.ChunkFile.Observe = null;
        }

        var reloaded = await world.GetChunkAsync(64, 64, scheduleGeneration: false);
        Assert.NotNull(reloaded);
        Assert.NotSame(chunk, reloaded);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(1, 64, 1).RegistryId);
        Assert.NotNull(reloaded.GetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task FailedUnloadRetriesLiveAndPendingEntities(
        bool wholeRegion, bool entityFile, bool pendingEntity)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-entities-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        await region.FlushAsync();

        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        AddSpawner(chunk);
        if (pendingEntity)
            chunk.PendingEntities.Add(new GeneratedEntity("minecraft:end_crystal", new VectorD(1032, 80, 1032)));

        var item = new ItemEntity
        {
            Level = world,
            EntityId = 1,
            Position = new VectorF(1032, 65, 1032),
            Item = new ItemStack(ItemsRegistry.Apple, 3)
        };
        Assert.True(region.TryAddEntity(item));
        var failingFile = entityFile ? region.EntityFile : region.ChunkFile;
        failingFile.Observe = change => change.Step == RegionFile.Step.PayloadWritten
            ? ValueTask.FromException(new IOException("The unload write failed."))
            : ValueTask.CompletedTask;

        await Assert.ThrowsAsync<IOException>(() => StartUnload(world, region, wholeRegion));
        Assert.Same(chunk, region.GetLoadedChunk(0, 0));
        failingFile.Observe = null;

        // Region failures retry through autosave; chunk failures keep their resident snapshot for the next save.
        await world.FlushRegionsAsync();
        await world.DisposeAsync();

        // Read the autosave's disk state without taking another unloading snapshot that could hide a missed retry.
        await using var reader = new Region(region.X, region.Z,
            Path.GetDirectoryName(region.RegionFolder)!, Path.GetFileName(region.RegionFolder));
        await reader.InitAsync();
        var reloaded = (Chunk)await reader.GetChunkAsync(0, 0);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(1, 64, 1).RegistryId);
        Assert.NotNull(reloaded.GetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z));
        Assert.Equal(pendingEntity ? 2 : 1, reloaded.PendingEntities.Count);
        var savedItem = Assert.Single(reloaded.PendingEntities, entity => entity.Type == "minecraft:item");
        var restoredItem = Assert.IsType<ItemEntity>(EntityNbt.Load(EntityNbt.ToNbt(savedItem), world));
        Assert.Equal(3, restoredItem.Item.Count);
        Assert.Equal(ItemsRegistry.Apple, restoredItem.Item.Holder);
        if (pendingEntity)
            Assert.Contains(reloaded.PendingEntities, entity => entity.Type == "minecraft:end_crystal");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UnloadWaitsForAFailingEarlierSaveWithoutHoldingTheTick(bool wholeRegion, bool eager)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-prior-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        await region.FlushAsync();
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);

        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = 1;
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != RegionFile.Step.Flushed || Interlocked.Exchange(ref failing, 0) == 0)
                return;

            writing.TrySetResult();
            await release.Task;
            throw new IOException("The earlier save failed.");
        };

        var earlierSave = eager ? SaveEagerlyAsync(region, chunk) : world.FlushRegionsAsync();
        Task unload = Task.CompletedTask;
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            chunk.SetBlock(2, 64, 2, BlocksRegistry.Stone);
            unload = StartUnload(world, region, wholeRegion);
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(unload.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => earlierSave.WaitAsync(TimeSpan.FromSeconds(10)));
            await unload.WaitAsync(TimeSpan.FromSeconds(10));
            region.ChunkFile.Observe = null;
        }

        var reloaded = await world.GetChunkAsync(64, 64, scheduleGeneration: false);
        Assert.NotNull(reloaded);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(1, 64, 1).RegistryId);
        Assert.Equal(BlockIds.Stone, reloaded.GetBlock(2, 64, 2).RegistryId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalWaitsForAnUnloadWrite(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-dispose-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.Flushed);

        var unload = StartUnload(world, region, wholeRegion);
        Task disposing = Task.CompletedTask;
        try
        {
            await writing.WaitAsync(TimeSpan.FromSeconds(10));
            disposing = world.DisposeAsync().AsTask();
            var first = await Task.WhenAny(disposing, Task.Delay(100));
            Assert.NotSame(disposing, first);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(unload, disposing).WaitAsync(TimeSpan.FromSeconds(10));
        }

        await using var reader = new Region(region.X, region.Z,
            Path.GetDirectoryName(region.RegionFolder)!, Path.GetFileName(region.RegionFolder));
        await reader.InitAsync();
        Assert.Equal(BlockIds.Stone, (await reader.GetChunkAsync(0, 0)).GetBlock(1, 64, 1).RegistryId);
    }

    private static Region PrepareUnload(World world, out Chunk chunk)
    {
        chunk = Complete(64, 64);
        var region = (Region)world.LoadRegionByChunk(64, 64);
        region.SetChunk(chunk);
        world.LoadedChunks.Add(NumericsHelper.IntsToLong(64, 64));
        world.LevelData.GameTime = 600;
        return region;
    }

    private static Task StartUnload(World world, Region region, bool wholeRegion) => wholeRegion
        ? world.UnloadRegionAsync(region.X, region.Z)
        : world.ManageChunksAsync();

    private static (Task Writing, TaskCompletionSource Release) PauseUnloadWrite(Region region, RegionFile.Step step)
    {
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != step)
                return;

            writing.TrySetResult();
            await release.Task;
        };
        return (writing.Task, release);
    }

    // Saves the chunk like a tick's eager save (see AbstractLevel.SaveChunksEagerlyAsync), waiting for its writes.
    // An attempt can leave the chunk out while other tests hold the shared staging slots, so it's tried again, as a
    // later tick would.
    private static async Task SaveEagerlyAsync(Region region, Chunk chunk)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        List<IChunk> snapshotted;
        do
        {
            snapshotted = [];
            var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var written = region.TrySaveChunks([chunk], snapshotted, taken);

            Assert.NotNull(written);
            await written;
        }
        while (snapshotted.Count == 0 && DateTime.UtcNow < deadline);

        Assert.Equal([chunk], snapshotted);
    }

    private static Chunk Complete(int x, int z)
    {
        var chunk = new Chunk(x, z);
        chunk.SetChunkStatus(ChunkGenStage.full);
        return chunk;
    }

    // A complete chunk a level ticks, with a block tick due in 5 game ticks.
    private static Chunk Ticking(int x, BlockTickScheduler scheduler)
    {
        var chunk = Complete(x, 0);
        chunk.BlockTicks.Schedule(new Vector(x * 16 + 1, 64, 1), "minecraft:stone", 5);
        chunk.BlockTicks.StartTicking(scheduler, NumericsHelper.IntsToLong(x, 0));
        return chunk;
    }

    private static DataBlockEntity CreateSpawner() => (DataBlockEntity)BlockEntityNbt.Create("minecraft:mob_spawner", Spawner);

    private static void AddSpawner(Chunk chunk) => chunk.SetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z, CreateSpawner());

    // A plugin's container: saved with its items as they are, like vanilla's.
    private static void AddCrate(Chunk chunk) => chunk.SetBlockEntity(Crate.X, Crate.Y, Crate.Z,
        new ContainerBlockEntity(new Container(9)) { Id = "probe:crate", BlockPosition = Crate });
}
