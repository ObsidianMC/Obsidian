using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang.Structures;
using System.Collections.Generic;
using System.Linq;

namespace WorldgenBench;

internal enum SerialStage { Terrain, StructureStarts, Features, PostProcess, Light }

/// <summary>
/// The 26.3 biome, terrain and features statuses with structures and the extracted 26.3 templates, as
/// <see cref="BiomeTerrain.Generate"/> and <see cref="BiomeTerrain.Decorate"/> run them, with a final heightmap
/// handoff after all neighboring decoration has finished.
/// </summary>
internal sealed class BenchGenerator
{
    private readonly BiomeTerrain pipeline;

    public BenchGenerator(BenchDimension bench, long seed)
    {
        var dimension = WorldgenDimension.Vanilla(bench.Identifier);
        this.HasSkyLight = dimension.DimensionType.Element.HasSkylight;
        this.pipeline = BiomeTerrain.Create(dimension, seed, new FeatureRegistry(WorldgenData.Vanilla),
            structures: true);
    }

    public StructureManager? Structures => this.pipeline.Structures;

    /// <summary>Whether the dimension type has sky light (the nether doesn't).</summary>
    public bool HasSkyLight { get; }

    /// <summary>Generates a chunk's terrain, timing it.</summary>
    public IChunk Carve(int x, int z, StageTimer<SerialStage> timer)
    {
        var chunk = new Chunk(x, z, this.pipeline.MinY, this.pipeline.Height);
        timer.Time(SerialStage.Terrain, () => this.pipeline.Generate(chunk));
        return chunk;
    }

    /// <summary>Decorates a carved chunk whose eight neighbors are carved.</summary>
    public void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z) =>
        this.pipeline.Decorate(area, x, z);

    /// <summary>Runs what follows decoration before light.</summary>
    public void PostProcess(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z,
        StageTimer<SerialStage> timer)
    {
        timer.Time(SerialStage.PostProcess, () =>
        {
            this.pipeline.PostProcess(area, x, z);
            this.pipeline.CompleteHeightmaps(area[(x, z)]);
            this.pipeline.Populate(area, x, z);
        });
    }

    /// <summary>
    /// Starts the structures that are placed on first use and in parallel, so they stay out of the stages.
    /// </summary>
    public void PrepareStructures(int centerX, int centerZ)
    {
        if (this.Structures is StructureManager structures)
            _ = structures.GetStartsReaching(centerX, centerZ).Count();
    }
}
