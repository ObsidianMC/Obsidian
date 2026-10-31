using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace Obsidian.Tests;

/// <summary>
/// The 26.3 generators a fixture replay shares between its cases, built on first use: terrain by seed and noise
/// settings, and the biome pipelines by seed and dimension. Reading and seeding a router and building a biome tree
/// cost more than most cases do.
/// </summary>
/// <remarks>
/// It isn't thread safe: a replay keeps its own, or one per slice with <see cref="AssertAllInSlices"/>.
/// </remarks>
internal sealed class WorldgenPipelines
{
    private readonly Dictionary<(long Seed, string Settings), TerrainGenerator> terrain = [];
    private readonly Dictionary<(string Dimension, long Seed, PipelineKind Kind), BiomeTerrain> pipelines = [];

    private enum PipelineKind
    {
        Biomes,
        Decoration,
        Structures
    }

    // Fixed rather than one per core, so every machine replays each case after the same ones (see
    // AssertAllInSlices).
    private const int Slices = 4;

    /// <summary>
    /// Like <see cref="FixtureCases.AssertAll"/>, for cases that each generate terrain: a few slices of the cases at
    /// once, each slice in order on one thread with its own pipelines.
    /// </summary>
    /// <remarks>
    /// A biome search starts from the thread's previous result, which only matters for exact ties. A slice always
    /// holds the same cases in the same order, so a case sees the same history on every run.
    /// </remarks>
    public static void AssertAllInSlices<T>(
        IEnumerable<T> cases, Func<T, string> name, Action<T, WorldgenPipelines> check)
    {
        var all = cases.ToArray();
        var errors = new ExceptionDispatchInfo?[all.Length];

        Parallel.For(0, Slices, new ParallelOptions { MaxDegreeOfParallelism = Slices }, slice =>
        {
            var pipelines = new WorldgenPipelines();

            for (var index = slice; index < all.Length; index += Slices)
                errors[index] = Run(() => check(all[index], pipelines));
        });

        var indices = Enumerable.Range(0, all.Length);

        FixtureCases.AssertAll(indices, index => name(all[index]), index => errors[index]?.Throw());
    }

    /// <summary>The noise settings a dimension generates with, such as <c>minecraft:nether</c>.</summary>
    public static string Settings(string dimension) => dimension switch
    {
        "minecraft:the_nether" => "minecraft:nether",
        "minecraft:the_end" => "minecraft:end",
        _ => dimension
    };

    /// <summary>The biome preset of a dimension, such as <c>minecraft:nether</c>.</summary>
    public static string Preset(string dimension) =>
        dimension == "minecraft:the_nether" ? "minecraft:nether" : dimension;

    public TerrainGenerator Terrain(long seed, string settings)
    {
        if (this.terrain.TryGetValue((seed, settings), out var generator))
            return generator;

        generator = new TerrainGenerator(WorldgenData.Vanilla, seed, settings);
        this.terrain.Add((seed, settings), generator);

        return generator;
    }

    /// <summary>The biome and terrain statuses of a dimension, without features.</summary>
    public BiomeTerrain Biomes(string dimension, long seed) => this.Pipeline(dimension, seed, PipelineKind.Biomes);

    /// <summary>The biome, terrain and features statuses of a dimension, with structures off.</summary>
    public BiomeTerrain Decoration(string dimension, long seed) =>
        this.Pipeline(dimension, seed, PipelineKind.Decoration);

    /// <summary>The biome, terrain and features statuses of a dimension, with its structures.</summary>
    public BiomeTerrain Structures(string dimension, long seed) =>
        this.Pipeline(dimension, seed, PipelineKind.Structures);

    private BiomeTerrain Pipeline(string dimension, long seed, PipelineKind kind)
    {
        if (this.pipelines.TryGetValue((dimension, seed, kind), out var pipeline))
            return pipeline;

        var generator = this.Terrain(seed, Settings(dimension));
        var preset = Preset(dimension);

        pipeline = kind == PipelineKind.Biomes
            ? new BiomeTerrain(generator, preset)
            : new BiomeTerrain(generator, preset, Features(), structures: kind == PipelineKind.Structures);
        this.pipelines.Add((dimension, seed, kind), pipeline);

        return pipeline;
    }

    private static ExceptionDispatchInfo? Run(Action check)
    {
        try
        {
            check();
            return null;
        }
        catch (Exception error)
        {
            return ExceptionDispatchInfo.Capture(error);
        }
    }

    // The default template lookup reads a server's extracted templates; replays read 26.3's own.
    private static FeatureRegistry Features() => new(WorldgenData.Vanilla) { Template = VanillaFeatureTemplates.Get };
}
