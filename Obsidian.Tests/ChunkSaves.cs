using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
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
