using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockTicks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Scheduled block ticks (R1, see docs/redstone/01-foundation.md): due times, vanilla's drain order, duplicate and stale
/// ticks, the per-tick cap, persistence through <c>block_ticks</c>, and chunk unload and reload.
/// </summary>
public class BlockTicks
{
    private const string Stone = "minecraft:stone";
    private const string Dirt = "minecraft:dirt";

    [Fact]
    public void DueTickRunsOnItsGameTime()
    {
        var scheduler = new BlockTickScheduler();
        var chunk = new ChunkBlockTicks();
        chunk.Schedule(new Vector(1, 64, 1), Stone, delay: 3);
        chunk.StartTicking(scheduler, 0);

        var ran = new List<long>();
        for (var tick = 1; tick <= 4; tick++)
            scheduler.Tick(_ => true, due => ran.Add(scheduler.GameTime));

        Assert.Equal([3L], ran);
    }

    // Each row schedules work and ticks, recording what each tick ran as "x:block". Every tick runs at most once, and
    // whatever doesn't run (over the cap, or not due) is kept for a later tick rather than lost.
    [Fact]
    public void SchedulerRunsEachTickOnceAndKeepsLeftoverWork()
    {
        SchedulerCase[] cases =
        [
            new("duplicates run once per block type", RunDuplicates, PerTick: null, Ran: ["1:" + Stone, "1:" + Dirt]),
            new("work over the cap runs next tick", RunOverTheCap, PerTick: [2, 1],
                Ran: ["1:" + Stone, "2:" + Stone, "3:" + Stone]),
            new("a replaced block's tick is dropped", RunWithAReplacedBlock, PerTick: null, Ran: ["5:" + Stone])
        ];

        FixtureCases.AssertAll(cases, row => row.Name, AssertSchedulerRuns);
    }

    // A scheduler row: what each tick ran, how many runs each tick should have (when that matters) and every run.
    private sealed record SchedulerCase(string Name, Func<List<List<string>>> Run, int[]? PerTick, string[] Ran);

    private static void AssertSchedulerRuns(SchedulerCase row)
    {
        var ticks = row.Run();

        var ran = ticks.SelectMany(tick => tick).Order(StringComparer.Ordinal);
        Assert.Equal(row.Ran.Order(StringComparer.Ordinal), ran);
        if (row.PerTick is not null)
            Assert.Equal(row.PerTick, ticks.Select(tick => tick.Count));
    }

    private static List<List<string>> RunDuplicates()
    {
        var scheduler = new BlockTickScheduler();
        var chunk = new ChunkBlockTicks();
        var position = new Vector(1, 64, 1);
        chunk.Schedule(position, Stone, delay: 2);
        chunk.Schedule(position, Stone, delay: 5);
        chunk.Schedule(position, Dirt, delay: 5);
        chunk.StartTicking(scheduler, 0);

        return RunScheduler(scheduler, ticks: 6, maxTicks: BlockTickScheduler.MaxTicksPerTick);
    }

    private static List<List<string>> RunOverTheCap()
    {
        var scheduler = new BlockTickScheduler();
        var chunk = new ChunkBlockTicks();
        for (var x = 1; x <= 3; x++)
            chunk.Schedule(new Vector(x, 64, 1), Stone, delay: 1);

        chunk.StartTicking(scheduler, 0);

        return RunScheduler(scheduler, ticks: 2, maxTicks: 2);
    }

    private static List<List<string>> RunWithAReplacedBlock()
    {
        var level = new FakeLevel();
        var ticks = new LevelBlockTicks(level, new Lock());
        var runs = new List<List<string>>();
        ticks.Register(Stone, (position, _) => runs[^1].Add($"{position.X}:{Stone}"));
        var kept = new Vector(5, 64, 5);
        var replaced = new Vector(6, 64, 5);
        level.Set(kept, Stone);
        level.Set(replaced, Stone);
        TrackNeighborhood(ticks, level, 0, 0);
        ticks.Schedule(kept, Stone, delay: 2);
        ticks.Schedule(replaced, Stone, delay: 2);

        // The block at one position changes before its tick is due.
        level.Set(replaced, Dirt);
        for (var tick = 0; tick < 3; tick++)
        {
            runs.Add([]);
            ticks.Tick();
        }

        return runs;
    }

    private static List<List<string>> RunScheduler(BlockTickScheduler scheduler, int ticks, int maxTicks)
    {
        var runs = new List<List<string>>();
        for (var tick = 0; tick < ticks; tick++)
        {
            var ran = new List<string>();
            scheduler.Tick(_ => true, due => ran.Add($"{due.Position.X}:{due.Block}"), maxTicks);
            runs.Add(ran);
        }

        return runs;
    }

    [Fact]
    public void ChunksThatDoNotTickKeepTheirTicks()
    {
        var scheduler = new BlockTickScheduler();
        var chunk = new ChunkBlockTicks();
        chunk.StartTicking(scheduler, 0);
        chunk.Schedule(new Vector(1, 64, 1), Stone, delay: 1);

        var ran = 0;
        scheduler.Tick(_ => false, _ => ran++);
        scheduler.Tick(_ => false, _ => ran++);
        Assert.Equal(0, ran);

        scheduler.Tick(_ => true, _ => ran++);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void NbtRoundTripKeepsTicksInOrder()
    {
        var original = new ChunkBlockTicks();
        original.Schedule(new Vector(1, 64, 1), Stone, delay: 4, priority: TickPriority.VeryLow);
        original.Schedule(new Vector(2, 70, 3), Dirt, delay: 0, priority: TickPriority.ExtremelyHigh);
        original.Schedule(new Vector(15, -60, 15), Stone, delay: 9);

        var stream = new MemoryStream();
        using (var writer = new NbtWriterStream(stream, NbtCompression.None, "Root"))
        {
            original.Write(writer);
            writer.EndCompound();
        }

        var root = new NbtReader(new MemoryStream(stream.ToArray()), NbtCompression.None).ReadNextTag() as NbtCompound;
        Assert.NotNull(root);
        Assert.True(root.TryGetTag<NbtList>("block_ticks", out var list));

        var loaded = new ChunkBlockTicks();
        loaded.Read(list, 0, 0);

        Assert.Equal(original.Pack(), loaded.Pack());
        Assert.Equal(["minecraft:stone", "minecraft:dirt", "minecraft:stone"], loaded.Pack().Select(tick => tick.Block));
    }

    [Fact]
    public async Task BlockTicksSurviveRegionSaveAndReload()
    {
        // Chunk (35, 40) lies in region (1, 1), so the stored coordinates are absolute, not region-local.
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-block-ticks-{Guid.NewGuid():N}");
        try
        {
            var chunk = new Chunk(35, 40);
            chunk.SetChunkStatus(ChunkGenStage.full);
            chunk.BlockTicks.Schedule(new Vector(35 * 16 + 2, 64, 40 * 16 + 3), Stone, delay: 7, priority: TickPriority.High);
            chunk.BlockTicks.Schedule(new Vector(35 * 16 + 4, 64, 40 * 16 + 3), Dirt, delay: 0);
            var expected = chunk.BlockTicks.Pack();

            await using (var region = new Region(1, 1, folder))
            {
                await region.InitAsync();
                region.SetChunk(chunk);
                await region.FlushAsync();
            }

            await using var reopened = new Region(1, 1, folder);
            await reopened.InitAsync();
            var loaded = Assert.IsType<Chunk>(await reopened.GetChunkAsync(35 - (1 << Region.CubicRegionSizeShift), 40 - (1 << Region.CubicRegionSizeShift)));
            Assert.Equal(expected, loaded.BlockTicks.Pack());
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ReadDropsTicksOutsideTheChunkAndWithoutABlock()
    {
        var stream = new MemoryStream();
        using (var writer = new NbtWriterStream(stream, NbtCompression.None, "Root"))
        {
            writer.WriteListStart("block_ticks", NbtTagType.Compound, 3);
            WriteTick(writer, 1, 64, 1, Stone);
            WriteTick(writer, 40, 64, 1, Stone);
            WriteTick(writer, 2, 64, 2, "");
            writer.EndList();
            writer.EndCompound();
        }

        var root = new NbtReader(new MemoryStream(stream.ToArray()), NbtCompression.None).ReadNextTag() as NbtCompound;
        Assert.True(root!.TryGetTag<NbtList>("block_ticks", out var list));

        var loaded = new ChunkBlockTicks();
        loaded.Read(list, 0, 0);

        Assert.Equal([new SavedBlockTick(new Vector(1, 64, 1), Stone, 0, TickPriority.Normal)], loaded.Pack());
    }

    [Fact]
    public void ReloadedTicksKeepTheirRelativeDelays()
    {
        var first = new BlockTickScheduler();
        var chunk = new ChunkBlockTicks();
        chunk.StartTicking(first, 0);
        chunk.Schedule(new Vector(1, 64, 1), Stone, delay: 5);

        // Two game ticks pass before the chunk unloads: three ticks are left.
        first.Tick(_ => true, _ => { });
        first.Tick(_ => true, _ => { });
        chunk.StopTicking();
        Assert.Equal([new SavedBlockTick(new Vector(1, 64, 1), Stone, 3, TickPriority.Normal)], chunk.Pack());

        // Later, the chunk starts ticking in a scheduler at game time 10 and the tick is due 3 game ticks later.
        var later = new BlockTickScheduler();
        for (var tick = 0; tick < 10; tick++)
            later.Tick(_ => false, _ => { });

        chunk.StartTicking(later, 0);
        var ran = new List<long>();
        for (var tick = 0; tick < 4; tick++)
            later.Tick(_ => true, _ => ran.Add(later.GameTime));

        Assert.Equal([13L], ran);
    }

    [Fact]
    public void LevelTicksInUnloadedChunksAreKeptAndPrunedOnlyAfterUnloading()
    {
        var level = new FakeLevel();
        var ticks = new LevelBlockTicks(level, new Lock());
        var position = new Vector(5, 64, 5);
        level.Set(position, Stone);
        TrackNeighborhood(ticks, level, 0, 0);
        var chunk = level.LoadedChunk(0, 0);
        ticks.Schedule(position, Stone, delay: 1000);

        // The chunk unloads: its neighborhood stops ticking; the pruning pass takes its ticks out of the scheduler.
        level.Loaded.Clear();
        for (var tick = 0; tick < 100; tick++)
            ticks.Tick();

        Assert.False(chunk.BlockTicks.IsTicking);
        Assert.Equal(0, ticks.TrackedChunks);
        Assert.Equal([new SavedBlockTick(position, Stone, 1000 - 100, TickPriority.Normal)], chunk.BlockTicks.Pack());
    }

    // A chunk ticks only when it and its eight neighbors are tracked, like the level tracks loaded complete chunks.
    private static void TrackNeighborhood(LevelBlockTicks ticks, FakeLevel level, int x, int z)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                ticks.Track(level.LoadedChunk(x + dx, z + dz));
        }
    }

    private static void WriteTick(NbtWriterStream writer, int x, int y, int z, string block)
    {
        writer.WriteCompoundStart();
        writer.WriteString("i", block);
        writer.WriteInt("x", x);
        writer.WriteInt("y", y);
        writer.WriteInt("z", z);
        writer.WriteInt("t", 0);
        writer.WriteInt("p", 0);
        writer.EndCompound();
    }

    /// <summary>
    /// A level with real chunks that can be loaded and unloaded, serving the block reads block ticks make.
    /// </summary>
    private sealed class FakeLevel : IBlockTickLevel
    {
        private readonly Dictionary<long, Chunk> chunks = [];

        public HashSet<long> Loaded { get; } = [];

        /// <summary>
        /// The complete chunk at (<paramref name="x"/>, <paramref name="z"/>), created loaded with its neighbors, so it ticks.
        /// </summary>
        public Chunk LoadedChunk(int x, int z)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var key = NumericsHelper.IntsToLong(x + dx, z + dz);
                    if (!this.chunks.ContainsKey(key))
                        this.chunks[key] = NewCompleteChunk(x + dx, z + dz);

                    this.Loaded.Add(key);
                }
            }

            return this.chunks[NumericsHelper.IntsToLong(x, z)];
        }

        public void Set(Vector position, string block)
        {
            var chunk = this.LoadedChunk(position.X >> 4, position.Z >> 4);
            chunk.SetBlock(position.X, position.Y, position.Z, BlocksRegistry.Get(block));
        }

        public IBlock? GetLoadedBlock(Vector position)
        {
            var key = NumericsHelper.IntsToLong(position.X >> 4, position.Z >> 4);
            return this.Loaded.Contains(key) && this.chunks.TryGetValue(key, out var chunk)
                ? chunk.GetBlock(position.X, position.Y, position.Z)
                : null;
        }

        public bool IsChunkLoaded(long key) => this.Loaded.Contains(key);

        public bool IsChunkSimulated(long key) => this.Loaded.Contains(key);

        // The constructor's status argument isn't applied, so completion is set explicitly.
        private static Chunk NewCompleteChunk(int x, int z)
        {
            var chunk = new Chunk(x, z);
            chunk.SetChunkStatus(ChunkGenStage.full);
            return chunk;
        }
    }
}
