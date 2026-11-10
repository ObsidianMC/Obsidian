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
using System.Collections.Concurrent;
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
            // Observe persisted payloads rather than preparatory structure serialization.
            var written = new ConcurrentQueue<(int X, int Z)>();
            var region = new Region(0, 0, folder);

            await using (region)
            {
                await region.InitAsync();
                region.ChunkFile.Observe = change =>
                {
                    if (change.Step == RegionFile.Step.PayloadWritten)
                        written.Enqueue((change.ChunkX, change.ChunkZ));

                    return default;
                };

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
            var written = new ConcurrentQueue<(int X, int Z)>();
            var scheduler = new BlockTickScheduler();
            var region = new Region(0, 0, folder)
            {
                // Like the level's, which stops an unloading chunk's ticks as its final save starts.
                BlockAccessUnloaded = chunk => ((Chunk)chunk).BlockTicks.StopTicking(),
            };

            await using (region)
            {
                await region.InitAsync();
                region.ChunkFile.Observe = change =>
                {
                    if (change.Step == RegionFile.Step.PayloadWritten)
                        written.Enqueue((change.ChunkX, change.ChunkZ));

                    return default;
                };

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

            // While it writes, the chunk changes again and unloads. The unload's snapshot takes the whole chunk, since
            // the autosave's write is still in flight, and its own write waits for that one's turn to end.
            chunk.SetBlock(2, 64, 2, BlocksRegistry.Stone);
            unload = region.UnloadChunk(0, 0);
            await Task.WhenAny(unload, Task.Delay(100));
            Assert.False(unload.IsCompleted);
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

        // The chunk is gone either way; its final save stays pending, for the next save to retry.
        await Assert.ThrowsAsync<IOException>(() => StartUnload(world, region, wholeRegion));
        Assert.Null(region.GetLoadedChunk(0, 0));
        Assert.Single(world.PendingWrites.Failed());
        failingFile.Observe = null;

        await world.FlushRegionsAsync();
        Assert.Empty(world.PendingWrites.Failed());
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

    [Fact]
    public async Task AnEarlierSaveFailingAsTheUnloadTakesTheMarksIsStillSaved()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-race-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = Complete(0, 0);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.SetChunk(chunk);
        await region.FlushAsync();
        AddSpawner(chunk);

        // An eager save takes the spawner; its commit waits until released, then fails.
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = 1;
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != RegionFile.Step.Flushed || Interlocked.Exchange(ref failing, 0) == 0)
                return;

            writing.TrySetResult();
            await release.Task;
            throw new IOException("The eager save failed.");
        };

        var eager = SaveEagerlyAsync(region, chunk);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // It fails, and its write ends, just as the unload's snapshot has taken the chunk's marks.
        region.UnloadMarksTaken = () =>
        {
            release.TrySetResult();
            var ended = SpinWait.SpinUntil(() => !region.ChunkFile.HasWriteInFlight(0, 0), TimeSpan.FromSeconds(10));
            Assert.True(ended);
        };

        try
        {
            await region.UnloadChunk(0, 0).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            region.UnloadMarksTaken = null;
            release.TrySetResult();
            region.ChunkFile.Observe = null;
        }

        await Assert.ThrowsAnyAsync<Exception>(() => eager.WaitAsync(TimeSpan.FromSeconds(10)));
        var reloaded = await region.GetChunkAsync(0, 0);
        Assert.NotSame(chunk, reloaded);
        Assert.NotNull(reloaded.GetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalRetriesAnUnloadWriteThatFailsWhileItWaits(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-dispose-fail-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        await world.FlushRegionsAsync();
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        chunk.NextEagerSave = long.MaxValue;

        // Nothing else is due for a final save. The unload's write waits until released, then fails once.
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = 1;
        region.ChunkFile.Observe = async change =>
        {
            if (change.Step != RegionFile.Step.PayloadWritten || Interlocked.Exchange(ref failing, 0) == 0)
                return;

            writing.TrySetResult();
            await release.Task;
            throw new IOException("The unload write failed.");
        };

        var unload = StartUnload(world, region, wholeRegion);
        Task disposing = Task.CompletedTask;
        try
        {
            // Disposal starts while the write is paused, and waits for it.
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            disposing = world.DisposeAsync().AsTask();
            await Task.WhenAny(disposing, Task.Delay(200));
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThrowsAsync<IOException>(() => unload.WaitAsync(TimeSpan.FromSeconds(10)));
        await disposing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(world.PendingWrites.Failed());

        await using var reader = new Region(region.X, region.Z,
            Path.GetDirectoryName(region.RegionFolder)!, Path.GetFileName(region.RegionFolder));
        await reader.InitAsync();
        Assert.Equal(BlockIds.Stone, (await reader.GetChunkAsync(0, 0)).GetBlock(1, 64, 1).RegistryId);
    }

    [Fact]
    public async Task ARegionWhoseFilesFailToOpenStaysUsableAfterItsUnloadFails()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-broken-{Guid.NewGuid():N}");
        var world = context.World;
        var folder = ((Region)world.LoadRegionByChunk(0, 0)).RegionFolder;

        // A truncated header holding data, which opening the region's chunk file refuses.
        await File.WriteAllBytesAsync(Path.Join(folder, "r.2.2.mca"), Enumerable.Repeat((byte)0xFF, 100).ToArray());
        var broken = (Region)world.LoadRegionByChunk(64, 64);

        await Assert.ThrowsAsync<InvalidDataException>(() => world.UnloadRegionAsync(2, 2));

        // Left as it was rather than retired: structure lookups fail at once instead of waiting for its unload.
        Assert.Same(broken, world.GetRegionForChunk(64, 64));
        var lookup = world.ReadSavedStructureStartsAsync(64, 64).AsTask();
        await Assert.ThrowsAsync<InvalidDataException>(() => lookup.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ARegionUnloadKeepsTheRegionWhenAChunkSnapshotFails()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-keep-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var unloading);
        var failing = Complete(65, 64);
        region.SetChunk(failing);
        await world.FlushRegionsAsync();

        // Both chunks change; one holds an entity whose save throws, so its chunk's final snapshot does too, after
        // another entity's save there succeeded.
        unloading.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        failing.SetBlock(2, 64, 2, BlocksRegistry.Stone);
        var saving = new ItemEntity
        {
            Level = world,
            EntityId = 1,
            Position = new VectorF(1048, 65, 1032),
            Item = new ItemStack(ItemsRegistry.Apple, 3)
        };
        var entity = new FailingItem
        {
            Level = world,
            EntityId = 2,
            Position = new VectorF(1048, 65, 1032),
            Item = new ItemStack(ItemsRegistry.Apple, 5)
        };
        Assert.True(world.TryAddEntity(saving));
        Assert.True(world.TryAddEntity(entity));

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.UnloadRegionAsync(region.X, region.Z));

        // The whole-region transaction keeps both chunks and their entities when either snapshot fails.
        Assert.Same(region, world.GetRegionForChunk(64, 64));
        Assert.Same(unloading, region.GetLoadedChunk(0, 0));
        Assert.Same(failing, region.GetLoadedChunk(1, 0));
        Assert.Same(saving, world.FindEntity(saving.Uuid));
        Assert.Same(entity, world.FindEntity(entity.Uuid));

        // Once the entity saves again, a later unload takes the chunk with its change.
        entity.Fail = false;
        await world.UnloadRegionAsync(region.X, region.Z);
        Assert.Null(world.GetRegionForChunk(64, 64));

        await world.DisposeAsync();
        await using var reader = new Region(region.X, region.Z,
            Path.GetDirectoryName(region.RegionFolder)!, Path.GetFileName(region.RegionFolder));
        await reader.InitAsync();
        Assert.Equal(BlockIds.Stone, (await reader.GetChunkAsync(0, 0)).GetBlock(1, 64, 1).RegistryId);
        var saved = (Chunk)await reader.GetChunkAsync(1, 0);
        Assert.Equal(BlockIds.Stone, saved.GetBlock(2, 64, 2).RegistryId);
        Assert.Equal(2, saved.PendingEntities.Count(pending => pending.Type == "minecraft:item"));
    }

    [Fact]
    public async Task ARegionThatCantReopenForARetryDoesntStopOtherSaves()
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-reopen-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        var healthyRegion = (Region)world.LoadRegionByChunk(0, 0);
        var healthy = Complete(0, 0);
        healthyRegion.SetChunk(healthy);
        await world.FlushRegionsAsync();

        // The region's final write fails, leaving its save pending, then its file can't be opened for a while.
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        region.ChunkFile.Observe = change => change.Step == RegionFile.Step.PayloadWritten
            ? ValueTask.FromException(new IOException("The unload write failed."))
            : ValueTask.CompletedTask;
        await Assert.ThrowsAsync<IOException>(() => world.UnloadRegionAsync(region.X, region.Z));
        var held = File.Open(Path.Join(region.RegionFolder, $"r.{region.X}.{region.Z}.mca"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // The autosave reports the region it couldn't reopen, and saves the others all the same.
        healthy.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        try
        {
            var failure = await Record.ExceptionAsync(() => world.FlushRegionsAsync());
            Assert.IsType<AggregateException>(failure);
            Assert.False(healthy.IsUnsaved);
            Assert.Single(world.PendingWrites.Failed());
        }
        finally
        {
            held.Dispose();
        }

        await world.FlushRegionsAsync();
        Assert.Empty(world.PendingWrites.Failed());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangesThroughTheLevelDuringAnUnloadWriteAreSaved(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-write-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        await region.FlushAsync();
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);

        // An eager save on the tick could write the change too; here only the unload and the reloaded chunk may.
        chunk.NextEagerSave = long.MaxValue;
        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.PayloadWritten);

        var unload = StartUnload(world, region, wholeRegion);
        try
        {
            // While the final save is written, a ticker gets the chunk through the level: a new instance, read from the
            // pending save, which it changes. The unloaded instance is out of reach.
            await writing.WaitAsync(TimeSpan.FromSeconds(10));
            await TickWithAsync(world, async () =>
            {
                var reloaded = await world.GetChunkAsync(64, 64, scheduleGeneration: false);
                Assert.NotNull(reloaded);
                Assert.NotSame(chunk, reloaded);
                Assert.Equal(BlockIds.Stone, reloaded.GetBlock(1, 64, 1).RegistryId);
                reloaded.SetBlock(2, 64, 2, BlocksRegistry.Stone);
            });
        }
        finally
        {
            release.TrySetResult();
            await unload.WaitAsync(TimeSpan.FromSeconds(10));
            region.ChunkFile.Observe = null;
        }

        var saved = await ReadSavedChunkAsync(world, region);
        Assert.Equal(BlockIds.Stone, saved.GetBlock(1, 64, 1).RegistryId);
        Assert.Equal(BlockIds.Stone, saved.GetBlock(2, 64, 2).RegistryId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EntityArrivingDuringAnUnloadWriteIsSaved(bool wholeRegion, bool movedIn)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-arrival-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        await region.FlushAsync();
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.PayloadWritten);

        // Spawned into the chunk, or moved in from chunk 63 of the neighbouring region, which stays loaded.
        var destination = new VectorD(1032, 65, 1032);
        var item = new ItemEntity
        {
            Level = world,
            EntityId = 1,
            Position = movedIn ? new VectorF(1020, 65, 1032) : (VectorF)destination,
            Item = new ItemStack(ItemsRegistry.Apple, 3)
        };
        if (movedIn)
        {
            ((Region)world.LoadRegionByChunk(63, 64)).SetChunk(Complete(63, 64));
            Assert.True(world.TryAddEntity(item));
        }

        var unload = StartUnload(world, region, wholeRegion);
        try
        {
            // The chunk's entities left with its final save; the item arrives in the chunk loaded again meanwhile.
            await writing.WaitAsync(TimeSpan.FromSeconds(10));
            await TickWithAsync(world, async () =>
            {
                Assert.NotNull(await world.GetChunkAsync(64, 64, scheduleGeneration: false));
                Arrive(world, item, destination, movedIn);
            });
        }
        finally
        {
            release.TrySetResult();
            await unload.WaitAsync(TimeSpan.FromSeconds(10));
            region.ChunkFile.Observe = null;
        }

        Assert.Same(item, world.FindEntity(item.Uuid));
        var saved = await ReadSavedChunkAsync(world, region);
        var savedItem = Assert.Single(saved.PendingEntities, entity => entity.Type == "minecraft:item");
        var restoredItem = Assert.IsType<ItemEntity>(EntityNbt.Load(EntityNbt.ToNbt(savedItem), world));
        Assert.Equal(3, restoredItem.Item.Count);
        Assert.Equal(ItemsRegistry.Apple, restoredItem.Item.Holder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnloadedSpawnersStopTickingWhileTheirSaveIsWritten(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-spawner-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        var spawner = (DataBlockEntity)BlockEntityNbt.Create("minecraft:mob_spawner", new Vector(1028, 30, 1028));
        chunk.SetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z, spawner);
        var watcher = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "Watcher", new VectorD(1028.5, 31, 1028.5));
        Assert.True(world.TryAddPlayer(watcher));

        // With its chunk loaded, a player in range counts the spawner down each tick.
        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var counted = Delay(spawner);
        Assert.True(counted < 20);

        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        chunk.NextEagerSave = long.MaxValue;
        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.PayloadWritten);
        var unload = StartUnload(world, region, wholeRegion);
        try
        {
            // Unloaded, it no longer ticks, so the save being written keeps its state.
            await writing.WaitAsync(TimeSpan.FromSeconds(10));
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(counted, Delay(spawner));
        }
        finally
        {
            release.TrySetResult();
            await unload.WaitAsync(TimeSpan.FromSeconds(10));
            region.ChunkFile.Observe = null;
        }

        world.Players.TryRemove(watcher.Uuid, out _);
        var saved = await ReadSavedChunkAsync(world, region);
        var savedSpawner = Assert.IsType<DataBlockEntity>(saved.GetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z));
        Assert.Equal(counted, Delay(savedSpawner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnloadCallbacksRunBetweenTicks(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-callback-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        var probe = new GateProbe(world) { Id = "probe:gate", BlockPosition = Crate };
        chunk.SetBlockEntity(Crate.X, Crate.Y, Crate.Z, probe);
        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await StartUnload(world, region, wholeRegion).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(probe.Unloaded);
        Assert.True(probe.GateHeld);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AChunkChangedEveryTickStillUnloads(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"chunk-saves-unload-busy-{Guid.NewGuid():N}");
        var world = context.World;
        var region = PrepareUnload(world, out var chunk);
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        chunk.NextEagerSave = long.MaxValue;

        // A plugin keeps writing the instance it holds, every tick, through and after the unload.
        var height = 70;
        using var writer = world.AddTicker(() =>
        {
            chunk.SetBlock(3, height++ % 200, 3, BlocksRegistry.Stone);
            return ValueTask.CompletedTask;
        });

        var (writing, release) = PauseUnloadWrite(region, RegionFile.Step.PayloadWritten);
        var unload = StartUnload(world, region, wholeRegion);
        try
        {
            await writing.WaitAsync(TimeSpan.FromSeconds(10));
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult();
        }

        // The unload ends while the level keeps ticking, and the autosave after it isn't held up.
        for (var tick = 0; tick < 100 && !unload.IsCompleted; tick++)
            await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await unload.WaitAsync(TimeSpan.FromSeconds(10));
        region.ChunkFile.Observe = null;
        await world.FlushRegionsAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // The instance the plugin holds kept changing after the unload took it out of the level.
        Assert.True(chunk.IsUnsaved);
    }

    private static void Arrive(World world, ItemEntity item, VectorD destination, bool movedIn)
    {
        if (!movedIn)
        {
            Assert.True(world.TryAddEntity(item));
            return;
        }

        Assert.True(world.TryMoveEntity(item, item.Position, destination));
        item.Position = (VectorF)destination;
    }

    // Saves and disposes the world, as a server stopping does, then reads the unloaded chunk's region from disk.
    private static async Task<Chunk> ReadSavedChunkAsync(World world, Region region)
    {
        await world.FlushRegionsAsync();
        await world.DisposeAsync();

        await using var reader = new Region(region.X, region.Z,
            Path.GetDirectoryName(region.RegionFolder)!, Path.GetFileName(region.RegionFolder));
        await reader.InitAsync();
        return (Chunk)await reader.GetChunkAsync(0, 0);
    }

    private static short Delay(DataBlockEntity spawner) =>
        spawner.Data.TryGetTagValue<short>("Delay", out var delay) ? delay : (short)20;

    // Runs one level tick in which a ticker does what's given once, as a plugin's ticker would.
    private static async Task TickWithAsync(World world, Func<ValueTask> action)
    {
        var ran = 0;
        using var ticker = world.AddTicker(async () =>
        {
            if (Interlocked.Exchange(ref ran, 1) == 0)
                await action();
        });

        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, ran);
    }

    // An item whose save throws while Fail is set, as a plugin entity's can.
    private sealed class FailingItem : ItemEntity
    {
        public bool Fail { get; set; } = true;

        internal override void WriteNbt(NbtCompound tag)
        {
            if (this.Fail)
                throw new InvalidOperationException("The entity's save failed.");

            base.WriteNbt(tag);
        }
    }

    /// <summary>
    /// Records whether its chunk's unload told it so between ticks, holding the level's simulation gate, where block
    /// entity callbacks run.
    /// </summary>
    private sealed class GateProbe(World world) : BlockEntity
    {
        public bool Unloaded { get; private set; }

        public bool GateHeld { get; private set; }

        protected internal override void OnChunkUnloaded()
        {
            this.Unloaded = true;

            // Takes the gate at once only when nothing holds it.
            var free = world.TryRunBetweenTicksAsync(() => { }, TimeSpan.Zero).GetAwaiter().GetResult();
            this.GateHeld = !free;
        }
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

    // Chunk management unloads unneeded chunks every 600 ticks, which ticks run before may have passed.
    private static Task StartUnload(World world, Region region, bool wholeRegion)
    {
        if (wholeRegion)
            return world.UnloadRegionAsync(region.X, region.Z);

        world.LevelData.GameTime = 600;
        return world.ManageChunksAsync();
    }

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
