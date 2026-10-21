using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        ["inhabited time"] = (null, chunk => chunk.InhabitedTime++),
        ["structure starts"] = (null, chunk => chunk.StructureStarts = new NbtCompound("starts")),
    };

    public static TheoryData<string> Changes => [.. changes.Keys];

    [Theory]
    [MemberData(nameof(Changes))]
    public void ChangesMarkTheChunkUnsaved(string change)
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

    [Fact]
    public async Task FlushWritesOnlyChangedChunks()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-chunk-saves-{Guid.NewGuid():N}");
        try
        {
            // The region asks for the starts of each chunk it writes, and only those.
            var written = new List<(int X, int Z)>();
            await using (var region = new Region(0, 0, folder) { SaveStructureStarts = (x, z, loaded) => { written.Add((x, z)); return loaded; } })
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
                Assert.Equal(BlockIds.Stone, (await reloaded.GetChunkAsync(0, 0)).GetBlock(2, 70, 3).RegistryId);
                Assert.NotNull(await reloaded.GetChunkAsync(1, 0));
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
                region.Entities.TryAdd(item.EntityId, item);

                await region.FlushAsync();
                Assert.Equal(1, chunk.SavedEntityCount);

                // The chunk itself is unchanged, but its entity file still lists the item.
                region.Entities.TryRemove(item.EntityId, out _);
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

    private static Chunk Complete(int x, int z)
    {
        var chunk = new Chunk(x, z);
        chunk.SetChunkStatus(ChunkGenStage.full);
        return chunk;
    }

    private static DataBlockEntity CreateSpawner() => (DataBlockEntity)BlockEntityNbt.Create("minecraft:mob_spawner", Spawner);

    private static void AddSpawner(Chunk chunk) => chunk.SetBlockEntity(Spawner.X, Spawner.Y, Spawner.Z, CreateSpawner());

    // A plugin's container: saved with its items as they are, like vanilla's.
    private static void AddCrate(Chunk chunk) => chunk.SetBlockEntity(Crate.X, Crate.Y, Crate.Z,
        new ContainerBlockEntity(new Container(9)) { Id = "probe:crate", BlockPosition = Crate });
}
