using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
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
    private enum Stage { Biomes, Noise, Surface, Carvers, StructureStarts, Features, PostProcess, Light }

    /// <returns>Whether the hashes match the recorded ones, or there are none for this run.</returns>
    public static bool Run(BenchDimension bench, BenchOptions options)
    {
        if (!options.Cold)
            Warm(bench.Dimension, options.Seed + 1);

        var builder = new ChunkBuilder(bench.Dimension, options.Seed);

        // Stronghold rings are placed on first use, in parallel; placing them first keeps them out of the stages.
        var setup = Stopwatch.StartNew();
        if (builder.Structures is StructureManager structures)
            _ = structures.GetStartsReaching(bench.CenterX, bench.CenterZ).Count();
        setup.Stop();

        var generator = new SerialGenerator(builder, bench.Dimension);
        var order = new List<(int X, int Z)>();
        var gcCounts = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var gcPause = GC.GetTotalPauseDuration();
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

        var full = order.Count;
        Console.WriteLine($"== {bench.Name} serial, seed {options.Seed}, {full} chunks around ({bench.CenterX}, {bench.CenterZ}): " +
            $"{generator.CarvedCount} carved, {generator.DecoratedCount} decorated");
        Console.WriteLine($"generation {total.Elapsed.TotalSeconds:F2} s = {total.Elapsed.TotalMilliseconds / full:F2} ms per chunk; " +
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

        var matches = hasher.Hash == bench.ExpectedHash && hasher.NbtHash == bench.ExpectedNbtHash;
        Console.WriteLine($"hash {hasher.Hash} nbt {hasher.NbtHash}: " +
            (matches ? "match" : $"MISMATCH, expected hash {bench.ExpectedHash} nbt {bench.ExpectedNbtHash}"));
        return matches;
    }

    /// <summary>
    /// Runs every stage on a few chunks of another world, so the measured run starts with optimized code.
    /// </summary>
    private static void Warm(MojangDimension dimension, long seed)
    {
        var generator = new SerialGenerator(new ChunkBuilder(dimension, seed), dimension);
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
    private sealed class SerialGenerator(ChunkBuilder builder, MojangDimension dimension)
    {
        private readonly Dictionary<(int X, int Z), IChunk> carved = [];
        private readonly HashSet<(int X, int Z)> decorated = [];
        private readonly HashSet<(int X, int Z)> lit = [];

        public StageTimer<Stage> Timer { get; } = new();

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
            this.Timer.Time(Stage.PostProcess, () =>
            {
                builder.PostProcess(area, x, z);
                builder.UpdateFinalHeightmaps(chunk);
            });

            var neighbors = area.Where(entry => entry.Key != (x, z) && this.lit.Contains(entry.Key)).Select(entry => entry.Value).ToList();
            this.Timer.Time(Stage.Light, () => LightEngine.LightChunk(chunk, neighbors, dimension.HasSkyLight));
            this.lit.Add((x, z));
        }

        private void Decorate(int x, int z)
        {
            if (!this.decorated.Add((x, z)))
                return;

            var area = this.Area(x, z);

            // Starts are made on first use; asking for them first separates making them from placing them.
            if (builder.Structures is StructureManager structures)
                this.Timer.Time(Stage.StructureStarts, () => _ = structures.GetStartsReaching(x, z).Count());

            this.Timer.Time(Stage.Features, () => builder.Decorate(area, x, z));
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

            chunk = new Chunk(x, z, dimension.MinY, dimension.Height);
            this.Timer.Time(Stage.Biomes, () => builder.PopulateBiomes(chunk));
            this.Timer.Time(Stage.Noise, () => builder.Generate3DTerrain(chunk));
            this.Timer.Time(Stage.Surface, () => builder.ApplySurfaceRules(chunk));
            this.Timer.Time(Stage.Carvers, () => builder.ApplyCarvers(chunk));
            return this.carved[(x, z)] = chunk;
        }
    }
}
