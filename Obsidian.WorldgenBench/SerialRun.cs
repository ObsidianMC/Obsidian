using Obsidian.API;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Lighting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WorldgenBench;

/// <summary>
/// Generates a square of chunks to full on one thread and reports each stage's cost and the output's hashes.
/// </summary>
internal static class SerialRun
{
    /// <returns>Whether the hashes match the recorded ones, or there are none for this run.</returns>
    public static bool Run(BenchDimension bench, BenchOptions options)
    {
        if (!options.Cold)
            Warm(bench, options);

        var builder = new BenchGenerator(bench, options.Seed);

        // Stronghold rings are placed on first use, in parallel; placing them first keeps them out of the stages.
        var setup = Stopwatch.StartNew();
        builder.PrepareStructures(bench.CenterX, bench.CenterZ);
        setup.Stop();

        var generator = new SerialGenerator(builder);
        var order = new List<(int X, int Z)>();
        var gcCounts = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var gcPause = GC.GetTotalPauseDuration();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var x = bench.CenterX - options.Radius; x < bench.CenterX + options.Radius; x++)
        {
            for (var z = bench.CenterZ - options.Radius; z < bench.CenterZ + options.Radius; z++)
            {
                generator.Complete(x, z);
                order.Add((x, z));
            }
        }

        total.Stop();
        gcPause = GC.GetTotalPauseDuration() - gcPause;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        var full = order.Count;
        Console.WriteLine($"== {bench.Name} serial, seed {options.Seed}, " +
            $"{full} chunks around ({bench.CenterX}, {bench.CenterZ}): " +
            $"{generator.CarvedCount} carved, {generator.DecoratedCount} decorated");
        Console.WriteLine($"generation {total.Elapsed.TotalSeconds:F2} s = " +
            $"{total.Elapsed.TotalMilliseconds / full:F2} ms per chunk " +
            $"({full / total.Elapsed.TotalSeconds:F0} chunks/s), " +
            $"allocated {allocated / 1024.0 / full:F0} KB per chunk; " +
            $"structure setup {setup.Elapsed.TotalMilliseconds:F0} ms; GCs {GC.CollectionCount(0) - gcCounts.Item1}/" +
            $"{GC.CollectionCount(1) - gcCounts.Item2}/{GC.CollectionCount(2) - gcCounts.Item3}, paused {gcPause.TotalMilliseconds:F0} ms");
        generator.Timer.Print(full);

        var hasher = new ChunkHasher();
        foreach (var (x, z) in order)
            hasher.Add(generator.Get(x, z));

        if (!options.HasExpectedHashes)
        {
            Console.WriteLine($"hash {hasher.Hash} nbt {hasher.NbtHash}");
            return true;
        }

        var (expectedHash, expectedNbtHash) = (bench.ExpectedHash, bench.ExpectedNbtHash);
        var matches = hasher.Hash == expectedHash && hasher.NbtHash == expectedNbtHash;
        Console.WriteLine($"hash {hasher.Hash} nbt {hasher.NbtHash}: " +
            (matches ? "match" : $"MISMATCH, expected hash {expectedHash} nbt {expectedNbtHash}"));
        return matches;
    }

    /// <summary>
    /// Runs every stage on a few chunks of another world, so the measured run starts with optimized code.
    /// </summary>
    private static void Warm(BenchDimension bench, BenchOptions options)
    {
        var builder = new BenchGenerator(bench, options.Seed + 1);
        var generator = new SerialGenerator(builder);
        for (var x = 500; x < 503; x++)
        {
            for (var z = 500; z < 503; z++)
                generator.Complete(x, z);
        }
    }

    /// <summary>
    /// Completes chunks the way a server does: a chunk's neighbors are decorated once their own neighbors are carved, then
    /// the chunk is post-processed and lit, with the light of its neighbors lit before it.
    /// </summary>
    private sealed class SerialGenerator(BenchGenerator builder)
    {
        private readonly Dictionary<(int X, int Z), IChunk> carved = [];
        private readonly HashSet<(int X, int Z)> decorated = [];
        private readonly HashSet<(int X, int Z)> lit = [];

        public StageTimer<SerialStage> Timer { get; } = new();

        public int CarvedCount => this.carved.Count;

        public int DecoratedCount => this.decorated.Count;

        public IChunk Get(int x, int z) => this.carved[(x, z)];

        public void Complete(int x, int z)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    this.Decorate(x + dx, z + dz);
            }

            var chunk = this.carved[(x, z)];
            var area = this.Area(x, z);
            builder.PostProcess(area, x, z, this.Timer);

            var neighbors = area.Where(entry => entry.Key != (x, z) && this.lit.Contains(entry.Key)).Select(entry => entry.Value).ToList();
            this.Timer.Time(SerialStage.Light, () => LightEngine.LightChunk(chunk, neighbors, builder.HasSkyLight));
            this.lit.Add((x, z));
        }

        private void Decorate(int x, int z)
        {
            if (!this.decorated.Add((x, z)))
                return;

            var area = this.Area(x, z);

            // Starts are made on first use; asking for them first separates making them from placing them.
            if (builder.Structures is StructureManager structures)
                this.Timer.Time(SerialStage.StructureStarts, () => _ = structures.GetStartsReaching(x, z).Count());

            this.Timer.Time(SerialStage.Features, () => builder.Decorate(area, x, z));
        }

        private Dictionary<(int X, int Z), IChunk> Area(int x, int z)
        {
            var area = new Dictionary<(int X, int Z), IChunk>();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    area[(x + dx, z + dz)] = this.Carve(x + dx, z + dz);
            }

            return area;
        }

        private IChunk Carve(int x, int z)
        {
            if (this.carved.TryGetValue((x, z), out var chunk))
                return chunk;

            return this.carved[(x, z)] = builder.Carve(x, z, this.Timer);
        }
    }
}
