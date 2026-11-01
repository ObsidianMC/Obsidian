using System;
using System.Collections.Generic;
using Obsidian.Registries;
using Obsidian.WorldData.Generators.Features;
using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;

namespace Obsidian.Tests;

/// <summary>Serial terrain and decoration using the same statuses as the live generator.</summary>
internal sealed class NativeChunks(BiomeTerrain pipeline)
{
    private readonly Dictionary<(int X, int Z), IChunk> chunks = [];

    public static BiomeTerrain Pipeline(long seed, Func<WorldgenStructures> structures) =>
        BiomeTerrain.Create(WorldgenDimension.Overworld, seed,
            new FeatureRegistry(Obsidian.WorldData.Generators.Density.WorldgenData.Vanilla)
            {
                Template = id => structures().Templates.GetValueOrDefault(id) ?? StructureRegistry.GetCurrent(id)
            }, structures: true, structureSource: structures);

    public IChunk Get(int x, int z)
    {
        var chunk = this.Terrain(x, z);
        if (chunk.IsGenerated)
            return chunk;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                this.Decorate(x + dx, z + dz);
        }

        pipeline.PostProcess(this.chunks, x, z);
        pipeline.CompleteHeightmaps(chunk);
        chunk.SetChunkStatus(ChunkGenStage.full);
        return chunk;
    }

    private IChunk Terrain(int x, int z)
    {
        if (this.chunks.TryGetValue((x, z), out var found))
            return found;

        var chunk = new Chunk(x, z, pipeline.MinY, pipeline.Height);
        pipeline.Generate(chunk);
        this.chunks.Add((x, z), chunk);
        return chunk;
    }

    private void Decorate(int x, int z)
    {
        var chunk = this.Terrain(x, z);
        if (chunk.ChunkStatus >= ChunkGenStage.features)
            return;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                this.Terrain(x + dx, z + dz);
        }

        pipeline.Decorate(this.chunks, x, z);
    }
}
