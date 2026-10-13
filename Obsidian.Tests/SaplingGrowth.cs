using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Bone meal grows saplings into the trees vanilla's grow into, four dark oak saplings into one tree, and a plugin's
/// sapling into the feature it names while the plugin is loaded. Growth doesn't race players' block writes, queued
/// bone meal applies to the block each use finds, and random ticks only draw positions where vanilla's do.
/// </summary>
// Loading the probe plugin merges its tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class SaplingGrowth : IDisposable
{
    // Bone meal grows a sapling 45% of the time, a step at a time: this many uses grow one but for bad luck.
    private const int BoneMealUses = 64;

    private const int Ground = 69;

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-saplings-{Guid.NewGuid():N}");

    public SaplingGrowth() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(DisplayName = "Bone meal grows saplings into vanilla's trees, and dark oaks only from four",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BoneMealGrowsVanillaTrees()
    {
        await using var context = TestServerFactory.CreateServer($"saplings-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world, BlocksRegistry.GrassBlock);

        var oak = new Vector(12, Ground + 1, 12);
        world.GetLoadedChunk(0, 0)!.SetBlock(oak, BlocksRegistry.GetDefault(BlockIds.OakSapling));

        await GrowAsync(world, oak);

        // An oak (or, one time in ten, a fancy oak): logs from the sapling up, and leaves around them.
        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(oak).Is(BlockIds.OakLog));
        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(oak + Vector.Up).Is(BlockIds.OakLog));
        Assert.True(CountAround(world, oak, BlockIds.OakLeaves) > 0);

        // A dark oak sapling alone doesn't take bone meal's growth: it needs three more in a square.
        var corner = new Vector(3, Ground + 1, 3);
        var darkOak = BlocksRegistry.GetDefault(BlockIds.DarkOakSapling);
        world.GetLoadedChunk(0, 0)!.SetBlock(corner, darkOak);

        for (var use = 0; use < 8; use++)
        {
            Assert.True(world.UseBoneMeal(corner));
            await world.DoWorldTickAsync();
        }

        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(corner).Is(BlockIds.DarkOakSapling));

        foreach (var offset in new[] { Vector.East, Vector.South, new Vector(1, 0, 1) })
            world.GetLoadedChunk(0, 0)!.SetBlock(corner + offset, darkOak);

        await GrowAsync(world, corner + new Vector(1, 0, 1));

        foreach (var offset in new[] { Vector.Zero, Vector.East, Vector.South, new Vector(1, 0, 1) })
            Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(corner + offset).Is(BlockIds.DarkOakLog));

        // Bone meal on a grass block plants vanilla's grass_bonemeal around it, and ages a crop by two to five.
        var grass = new Vector(8, Ground, -8);
        Assert.True(world.UseBoneMeal(grass));
        await world.DoWorldTickAsync();
        Assert.True(CountAround(world, grass + Vector.Up, BlockIds.ShortGrass) > 0);

        var wheat = new Vector(-8, Ground + 1, 8);
        world.GetLoadedChunk(-1, 0)!.SetBlock(wheat, BlocksRegistry.GetDefault(BlockIds.Wheat));
        Assert.True(world.UseBoneMeal(wheat));
        await world.DoWorldTickAsync();
        Assert.InRange(int.Parse(world.GetLoadedChunk(-1, 0)!.GetBlock(wheat).GetProperty("age")!), 2, 5);
    }

    [Fact(DisplayName = "A plugin's sapling grows the feature it names until the plugin unloads",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginSaplingGrowsItsFeature()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-saplings-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world, BlocksRegistry.GrassBlock);

        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());

        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:sapling", out var sapling));
        var position = new Vector(8, Ground + 1, 8);
        world.GetLoadedChunk(0, 0)!.SetBlock(position, sapling.DefaultState);

        await GrowAsync(world, position);

        // probe:sapling_tree, the plugin's pillar of five oak logs, from where the sapling stood.
        for (var y = 0; y < 5; y++)
            Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(position + new Vector(0, y, 0)).Is(BlockIds.OakLog));

        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(position + new Vector(0, 5, 0)).IsAir);

        // Unloaded, the plugin's saplings stay in the world but don't grow.
        await manager.UnloadPluginAsync(container);

        var left = new Vector(12, Ground + 1, 4);
        world.GetLoadedChunk(0, 0)!.SetBlock(left, sapling.DefaultState);
        Assert.False(world.UseBoneMeal(left));
    }

    [Fact(DisplayName = "A player's break while bone meal grows a sapling isn't undone by the growth",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BreakDuringGrowthStaysBroken()
    {
        await using var context = TestServerFactory.CreateServer($"sapling-race-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world, BlocksRegistry.GrassBlock);

        var sapling = new Vector(8, Ground + 1, 8);
        world.GetLoadedChunk(0, 0)!.SetBlock(sapling, BlocksRegistry.GetDefault(BlockIds.OakSapling));

        // Loaded like a player's chunk, whose fluids tick, so that writes to it take only the locks they always do.
        await world.GetChunkAsync(0, 0, false);

        using var random = new PausingRandom();
        world.GrowthRandom = random;

        Assert.True(world.UseBoneMeal(sapling));
        var tick = Task.Run(world.DoWorldTickAsync);

        // The bone meal has read the sapling and is drawing its chance when the player breaks the sapling.
        Assert.True(random.Paused.Wait(TestServerFactory.TestTimeout));
        var write = Task.Run(async () => await world.SetBlockAsync(sapling, BlocksRegistry.Air));

        // The break waits for the growth, rather than land in between its read and its write.
        await Task.WhenAny(write, Task.Delay(200));
        Assert.False(write.IsCompleted);

        random.Resume();
        await Task.WhenAll(tick, write);

        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(sapling).IsAir);
    }

    [Fact(DisplayName = "Queued bone meal applies to the block each use finds, and is only used up when it applies",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task QueuedBoneMealAppliesToCurrentBlock()
    {
        await using var context = TestServerFactory.CreateServer($"bone-meal-queue-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world, BlocksRegistry.GrassBlock);

        // Three uses before a tick each age the wheat two to five from where the one before left it.
        var wheat = new Vector(-8, Ground + 1, 8);
        world.GetLoadedChunk(-1, 0)!.SetBlock(wheat, BlocksRegistry.GetDefault(BlockIds.Wheat));

        for (var use = 0; use < 3; use++)
            Assert.True(world.UseBoneMeal(wheat));

        await world.DoWorldTickAsync();
        Assert.True(int.Parse(world.GetLoadedChunk(-1, 0)!.GetBlock(wheat).GetProperty("age")!) >= 6);

        // A use whose wheat is broken before the tick does nothing, and the player keeps the bone meal.
        var player = new FakePlayer();
        player.Inventory.SetItem(0, ItemsRegistry.GetSingleItem(ItemsRegistry.BoneMeal));
        world.GetLoadedChunk(-1, 0)!.SetBlock(wheat, BlocksRegistry.GetDefault(BlockIds.Wheat));

        Assert.True(world.UseBoneMeal(wheat, player, 0));
        await world.SetBlockAsync(wheat, BlocksRegistry.Air);
        await world.DoWorldTickAsync();

        Assert.Equal(1, player.Inventory.GetItem(0)!.Count);
    }

    [Fact(DisplayName = "Random ticks draw positions only in sections with something that random-ticks",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task RandomTicksSkipSectionsWithoutTickingBlocks()
    {
        await using var context = TestServerFactory.CreateServer($"random-ticks-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world, BlocksRegistry.Stone);

        var player = new FakePlayer { Position = new VectorD(8, Ground + 1, 8) };
        Assert.True(world.TryAddPlayer(player));

        try
        {
            // Stone doesn't random-tick, so no section of the chunks around the player draws a position.
            var value = world.RandomTickValue;
            world.TickRandomBlocks();
            Assert.Equal(value, world.RandomTickValue);

            // A sapling's section in the middle chunk, whose neighbours are all there, draws the game rule's number
            // of positions; one in a chunk at the edge of those loaded doesn't tick.
            var sapling = BlocksRegistry.GetDefault(BlockIds.OakSapling);
            world.GetLoadedChunk(0, 0)!.SetBlock(new Vector(8, Ground + 1, 8), sapling);
            world.GetLoadedChunk(1, 1)!.SetBlock(new Vector(24, Ground + 1, 24), sapling);
            world.TickRandomBlocks();

            for (var tick = 0; tick < world.LevelData.GetIntegerRule("random_tick_speed", 3); tick++)
                value = unchecked(value * 3 + 1013904223);

            Assert.Equal(value, world.RandomTickValue);
        }
        finally
        {
            world.TryRemovePlayer(player);
        }
    }

    // Uses bone meal on the sapling, a tick at a time, until it's grown.
    private static async Task GrowAsync(WorldData.World world, Vector position)
    {
        for (var use = 0; use < BoneMealUses; use++)
        {
            if (!world.UseBoneMeal(position))
                return;

            await world.DoWorldTickAsync();
        }
    }

    private static int CountAround(WorldData.World world, Vector position, int block)
    {
        var count = 0;

        for (var x = -3; x <= 3; x++)
        {
            for (var y = 0; y <= 8; y++)
            {
                for (var z = -3; z <= 3; z++)
                {
                    var at = position + new Vector(x, y, z);
                    if (world.GetLoadedChunk(at.X >> 4, at.Z >> 4)!.GetBlock(at).Is(block))
                        count++;
                }
            }
        }

        return count;
    }

    // Complete plains chunks around the origin, empty but for a layer of the ground block.
    private static void AddChunks(WorldData.World world, IBlock ground)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);

                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, ground);

                        if (x % 4 != 0 || z % 4 != 0)
                            continue;

                        for (var y = -64; y < 320; y += 4)
                            chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
                    }
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }
    }

    // A random whose first draw waits until it's resumed and then comes up 0 (bone meal's chance comes up).
    private sealed class PausingRandom() : LegacyRandomSource(0), IDisposable
    {
        private readonly ManualResetEventSlim resumed = new();

        private int draws;

        public ManualResetEventSlim Paused { get; } = new();

        public void Resume() => this.resumed.Set();

        public override int Next(int bits)
        {
            if (Interlocked.Increment(ref this.draws) > 1)
                return base.Next(bits);

            this.Paused.Set();
            this.resumed.Wait(TestServerFactory.TestTimeout);
            return 0;
        }

        public void Dispose()
        {
            this.Paused.Dispose();
            this.resumed.Dispose();
        }
    }
}
