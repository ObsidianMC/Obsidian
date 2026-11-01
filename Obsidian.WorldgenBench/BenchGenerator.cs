using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Generators.Terrain;
using System.Collections.Generic;
using System.Linq;
using TerrainGenerator = Obsidian.WorldData.Generators.Terrain.TerrainGenerator;

namespace WorldgenBench;

internal enum SerialStage { Biomes, Noise, Surface, Carvers, StructureStarts, Features, PostProcess, Light, Terrain }

/// <summary>
/// The generator pipeline a serial run times: the retained 1.21.11 replay or the live 26.3 statuses.
/// </summary>
internal abstract class BenchGenerator
{
    public static BenchGenerator Create(BenchGeneratorKind kind, BenchDimension bench, long seed) => kind switch
    {
        BenchGeneratorKind.Current => new CurrentGenerator(bench, seed),
        _ => new LegacyGenerator(bench.Dimension, seed)
    };

    public abstract StructureManager? Structures { get; }

    /// <summary>Generates a chunk to the end of its carvers, timing each stage.</summary>
    public abstract IChunk Carve(int x, int z, StageTimer<SerialStage> timer);

    /// <summary>Decorates a carved chunk whose eight neighbors are carved.</summary>
    public abstract void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z);

    /// <summary>Runs what follows decoration before light, if the pipeline has such a stage.</summary>
    public abstract void PostProcess(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z,
        StageTimer<SerialStage> timer);

    /// <summary>
    /// Starts the structures that are placed on first use and in parallel, so they stay out of the stages.
    /// </summary>
    public void PrepareStructures(int centerX, int centerZ)
    {
        if (this.Structures is StructureManager structures)
            _ = structures.GetStartsReaching(centerX, centerZ).Count();
    }

    private sealed class LegacyGenerator(MojangDimension dimension, long seed) : BenchGenerator
    {
        private readonly ChunkBuilder builder = new(dimension, seed);

        public override StructureManager? Structures => this.builder.Structures;

        public override IChunk Carve(int x, int z, StageTimer<SerialStage> timer)
        {
            var chunk = new Chunk(x, z, dimension.MinY, dimension.Height);
            timer.Time(SerialStage.Biomes, () => this.builder.PopulateBiomes(chunk));
            timer.Time(SerialStage.Noise, () => this.builder.Generate3DTerrain(chunk));
            timer.Time(SerialStage.Surface, () => this.builder.ApplySurfaceRules(chunk));
            timer.Time(SerialStage.Carvers, () => this.builder.ApplyCarvers(chunk));
            return chunk;
        }

        public override void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z) =>
            this.builder.Decorate(area, x, z);

        public override void PostProcess(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z,
            StageTimer<SerialStage> timer)
        {
            timer.Time(SerialStage.PostProcess, () =>
            {
                this.builder.PostProcess(area, x, z);
                this.builder.UpdateFinalHeightmaps(area[(x, z)]);
            });
        }
    }

    /// <summary>
    /// The 26.3 biome, terrain and features statuses with structures and the extracted 26.3 templates, as
    /// <see cref="BiomeTerrain.Generate"/> and <see cref="BiomeTerrain.Decorate"/> run them, with a final heightmap
    /// handoff after all neighboring decoration has finished.
    /// </summary>
    private sealed class CurrentGenerator : BenchGenerator
    {
        private readonly BiomeTerrain pipeline;

        public CurrentGenerator(BenchDimension bench, long seed)
        {
            var identifier = bench.CurrentPreset == "minecraft:nether"
                ? "minecraft:the_nether" : bench.CurrentPreset;
            this.pipeline = BiomeTerrain.Create(WorldgenDimension.Vanilla(identifier), seed,
                new FeatureRegistry(WorldgenData.Vanilla), structures: true);
        }

        public override StructureManager? Structures => this.pipeline.Structures;

        public override IChunk Carve(int x, int z, StageTimer<SerialStage> timer)
        {
            var chunk = new Chunk(x, z, this.pipeline.MinY, this.pipeline.Height);
            timer.Time(SerialStage.Terrain, () => this.pipeline.Generate(chunk));
            return chunk;
        }

        public override void Decorate(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z) =>
            this.pipeline.Decorate(area, x, z);

        public override void PostProcess(IReadOnlyDictionary<(int X, int Z), IChunk> area, int x, int z,
            StageTimer<SerialStage> timer)
        {
            timer.Time(SerialStage.PostProcess, () =>
            {
                this.pipeline.PostProcess(area, x, z);
                this.pipeline.CompleteHeightmaps(area[(x, z)]);
                this.pipeline.Populate(area, x, z);
            });
        }
    }
}

internal enum BenchGeneratorKind
{
    /// <summary>The retained generator, on 1.21.11's worldgen data.</summary>
    Legacy,

    /// <summary>The 26.3 pipeline (<see cref="BiomeTerrain"/>).</summary>
    Current
}
