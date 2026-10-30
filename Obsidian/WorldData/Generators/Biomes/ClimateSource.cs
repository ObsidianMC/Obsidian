using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>26.3 biome selection over the float router, separate from the live legacy biome source.</summary>
internal sealed class ClimateSource : IBiomeSource
{
    private static readonly string[] climateFields =
        ["temperature", "vegetation", "continents", "erosion", "depth", "ridges"];

    private readonly DensityFunction[] functions;
    private readonly BiomeParameters? parameters;

    /// <param name="preset">minecraft:overworld, minecraft:nether, or minecraft:the_end.</param>
    public ClimateSource(WorldgenData data, DensityRouter router, string preset)
    {
        this.functions = climateFields.Select(field => router.Functions[field]).ToArray();
        this.parameters = preset == "minecraft:the_end" ? null : BiomeParameters.Read(data, preset);
    }

    public TargetPoint Sample(int quartX, int quartY, int quartZ)
    {
        var x = quartX << 2;
        var y = quartY << 2;
        var z = quartZ << 2;

        return new TargetPoint(
            Climate.Quantize(this.functions[0].Sample(x, y, z)),
            Climate.Quantize(this.functions[1].Sample(x, y, z)),
            Climate.Quantize(this.functions[2].Sample(x, y, z)),
            Climate.Quantize(this.functions[3].Sample(x, y, z)),
            Climate.Quantize(this.functions[4].Sample(x, y, z)),
            Climate.Quantize(this.functions[5].Sample(x, y, z)));
    }

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
    {
        var name = this.parameters is null
            ? this.EndBiome(quartX, quartY, quartZ)
            : this.parameters.Tree.Search(this.Sample(quartX, quartY, quartZ));

        return GetBiome(name);
    }

    /// <summary>
    /// Writes the BIOMES status before terrain. Climate uses a quart-spaced volume; search order is ascending
    /// section, then x, y, z within each section, matching vanilla's fillBiomesFromNoise tie history.
    /// </summary>
    public void FillBiomes(IChunk chunk)
    {
        var volume = new DensityVolume(4, chunk.Height >> 2, 4,
            chunk.X << 4, chunk.MinY, chunk.Z << 4, 4, 4, 4);
        var samples = this.parameters is null ? [] : this.SampleVolume(volume);

        for (var sectionY = 0; sectionY < chunk.Height; sectionY += 16)
        {
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    var quartY = (sectionY >> 2) + y;

                    for (var z = 0; z < 4; z++)
                    {
                        var index = volume.Index(x, quartY, z);
                        var biome = this.parameters is null
                            ? this.GetNoiseBiome((chunk.X << 2) + x, (chunk.MinY >> 2) + quartY,
                                (chunk.Z << 2) + z)
                            : GetBiome(this.parameters.Tree.Search(samples[index]));

                        chunk.SetBiome(x << 2, chunk.MinY + (quartY << 2), z << 2, biome);
                    }
                }
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.biomes);
    }

    private TargetPoint[] SampleVolume(DensityVolume volume)
    {
        var values = this.functions.Select(function =>
        {
            var buffer = new float[volume.Size];
            function.SampleVolume(buffer, volume);
            return buffer;
        }).ToArray();
        var targets = new TargetPoint[volume.Size];

        for (var index = 0; index < targets.Length; index++)
        {
            targets[index] = new TargetPoint(
                Climate.Quantize(values[0][index]),
                Climate.Quantize(values[1][index]),
                Climate.Quantize(values[2][index]),
                Climate.Quantize(values[3][index]),
                Climate.Quantize(values[4][index]),
                Climate.Quantize(values[5][index]));
        }

        return targets;
    }

    private string EndBiome(int quartX, int quartY, int quartZ)
    {
        var sectionX = quartX >> 2;
        var sectionZ = quartZ >> 2;

        if ((long)sectionX * sectionX + (long)sectionZ * sectionZ <= 4096L)
        {
            return "minecraft:the_end";
        }

        var erosion = this.functions[3].Sample(sectionX * 16 + 8, quartY << 2, sectionZ * 16 + 8);

        if (erosion > 0.25f)
        {
            return "minecraft:end_highlands";
        }

        if (erosion >= -0.0625f)
        {
            return "minecraft:end_midlands";
        }

        return erosion < -0.21875f ? "minecraft:small_end_islands" : "minecraft:end_barrens";
    }

    private static BiomeCodec GetBiome(string name) => CodecRegistry.TryGetBiome(name, out var biome)
        ? biome!
        : throw new InvalidOperationException($"Unknown biome '{name}'.");
}
