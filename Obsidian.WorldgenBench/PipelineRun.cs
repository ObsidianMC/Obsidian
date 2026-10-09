using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Lighting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;

namespace WorldgenBench;

/// <summary>
/// Generates a square of chunks on every core as a staged pipeline: every chunk is carved in parallel, then decorated, then
/// post-processed, then lit. The last three run in 9 waves, one per class of a 3 by 3 colouring of chunk positions, so the
/// chunks of a wave are 3 apart, their areas never overlap and nothing needs a lock. This bounds what the server's
/// pregeneration could reach on the same cores.
/// </summary>
/// <remarks>
/// Decorations that meet at chunk borders run in a different order than in <see cref="SerialRun"/>, so the output isn't
/// hashed.
/// </remarks>
internal static class PipelineRun
{
    private enum Stage { Biomes, Noise, Surface, Carvers, Features, PostProcess, Light }

    public static void Run(BenchDimension bench, BenchOptions options)
    {
        // The warm pass generates another world far away, so the measured pass runs with the JIT settled.
        if (!options.Cold)
            Generate(new ChunkBuilder(bench.Dimension, options.Seed + 7), bench.Dimension, bench.CenterX + 400, bench.CenterZ + 400,
                options.Radius);

        var builder = new ChunkBuilder(bench.Dimension, options.Seed);
        var result = Generate(builder, bench.Dimension, bench.CenterX, bench.CenterZ, options.Radius);

        Console.WriteLine($"== {bench.Name} pipeline on {Environment.ProcessorCount} processors, {(options.Cold ? "cold" : "warm")}, " +
            $"seed {options.Seed}, {result.Chunks} chunks: {result.Total.TotalSeconds:F2} s ({result.Chunks / result.Total.TotalSeconds:F0} chunks/s); " +
            $"carve {result.Carve.TotalSeconds:F2} s, decorate {result.Decorate.TotalSeconds:F2} s, " +
            $"finish {result.Finish.TotalSeconds:F2} s; GC paused {result.GcPause.TotalMilliseconds:F0} ms; " +
            $"JIT compiled {result.JitMethods} methods in {result.JitTime.TotalMilliseconds:F0} ms");
        Console.WriteLine("Per call times are summed over threads, so they grow when cores share caches, memory or SMT siblings.");
        result.Timer.Print(result.Chunks);
    }

    private static PipelineResult Generate(ChunkBuilder builder, MojangDimension dimension, int centerX, int centerZ, int radius)
    {
        // The full square, the decorated square around it and the carved square around that.
        var full = Square(centerX, centerZ, radius, 0);
        var decorated = Square(centerX, centerZ, radius, 1);
        var carved = Square(centerX, centerZ, radius, 2);

        var chunks = new ConcurrentDictionary<(int X, int Z), IChunk>();
        var lit = new ConcurrentDictionary<(int X, int Z), bool>();
        var timer = new StageTimer<Stage>();

        if (builder.Structures is StructureManager structures)
            _ = structures.GetStartsReaching(centerX, centerZ).Count();

        var gcPause = GC.GetTotalPauseDuration();
        var jitTime = JitInfo.GetCompilationTime();
        var jitMethods = JitInfo.GetCompiledMethodCount();
        var total = Stopwatch.StartNew();
        Parallel.ForEach(carved, position =>
        {
            var chunk = new Chunk(position.X, position.Z, dimension.MinY, dimension.Height);
            timer.Time(Stage.Biomes, () => builder.PopulateBiomes(chunk));
            timer.Time(Stage.Noise, () => builder.Generate3DTerrain(chunk));
            timer.Time(Stage.Surface, () => builder.ApplySurfaceRules(chunk));
            timer.Time(Stage.Carvers, () => builder.ApplyCarvers(chunk));
            chunks[position] = chunk;
        });

        var carve = total.Elapsed;
        InWaves(decorated, position => timer.Time(Stage.Features, () => builder.Decorate(Area(chunks, position), position.X, position.Z)));

        var decorate = total.Elapsed;
        InWaves(full, position => timer.Time(Stage.PostProcess, () =>
        {
            builder.PostProcess(Area(chunks, position), position.X, position.Z);
            builder.UpdateFinalHeightmaps(chunks[position]);
        }));
        InWaves(full, position =>
        {
            var neighbors = Area(chunks, position).Where(entry => entry.Key != position && lit.ContainsKey(entry.Key))
                .Select(entry => entry.Value).ToList();
            timer.Time(Stage.Light, () => LightEngine.LightChunk(chunks[position], neighbors, dimension.HasSkyLight));
            lit[position] = true;
        });

        total.Stop();
        return new PipelineResult(full.Count, total.Elapsed, carve, decorate - carve, total.Elapsed - decorate,
            GC.GetTotalPauseDuration() - gcPause, JitInfo.GetCompilationTime() - jitTime, JitInfo.GetCompiledMethodCount() - jitMethods, timer);
    }

    private static void InWaves(List<(int X, int Z)> positions, Action<(int X, int Z)> action)
    {
        for (var wave = 0; wave < 9; wave++)
            Parallel.ForEach(positions.Where(position => Wave(position) == wave), action);
    }

    private static int Wave((int X, int Z) position) => (position.X % 3 + 3) % 3 * 3 + (position.Z % 3 + 3) % 3;

    private static List<(int X, int Z)> Square(int centerX, int centerZ, int radius, int margin)
    {
        var positions = new List<(int X, int Z)>();
        for (var x = centerX - radius - margin; x < centerX + radius + margin; x++)
        {
            for (var z = centerZ - radius - margin; z < centerZ + radius + margin; z++)
                positions.Add((x, z));
        }

        return positions;
    }

    private static Dictionary<(int X, int Z), IChunk> Area(ConcurrentDictionary<(int X, int Z), IChunk> chunks, (int X, int Z) center)
    {
        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                area[(center.X + dx, center.Z + dz)] = chunks[(center.X + dx, center.Z + dz)];
        }

        return area;
    }

    private sealed record PipelineResult(int Chunks, TimeSpan Total, TimeSpan Carve, TimeSpan Decorate, TimeSpan Finish, TimeSpan GcPause,
        TimeSpan JitTime, long JitMethods,
        StageTimer<Stage> Timer);
}
