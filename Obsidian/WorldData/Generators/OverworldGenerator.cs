using Obsidian.WorldData.Decorators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Generators;

public sealed class OverworldGenerator : IWorldGenerator
{
    private GenHelper helper;

    public string Id => "overworld";

    public async Task<Chunk> GenerateChunkAsync(int cx, int cz, Chunk? chunk = null, ChunkStatus stage = ChunkStatus.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;
        if (helper is null)
            throw new NullReferenceException("GenHelper must not be null. Call Init()");

        chunk.chunkStatus = chunk.chunkStatus == ChunkStatus.empty ? ChunkStatus.structure_references : chunk.chunkStatus;

        if (ChunkStatus.biomes <= stage && chunk.chunkStatus < ChunkStatus.biomes)
        {
            ChunkBuilder.Biomes(helper, chunk);
            chunk.chunkStatus = ChunkStatus.biomes;
        }

        if (ChunkStatus.surface <= stage && chunk.chunkStatus < ChunkStatus.surface)
        {
            ChunkBuilder.Surface(helper, chunk);
            chunk.chunkStatus = ChunkStatus.surface;
        }

        if (ChunkStatus.carvers <= stage && chunk.chunkStatus < ChunkStatus.carvers)
        {
            ChunkBuilder.CavesAndOres(helper, chunk);
            ChunkBuilder.UpdateWGHeightmap(chunk);
            chunk.chunkStatus = ChunkStatus.carvers;
        }

        if (ChunkStatus.features <= stage && chunk.chunkStatus < ChunkStatus.features)
        {
            await OverworldDecorator.DecorateAsync(chunk, helper);
            chunk.chunkStatus = ChunkStatus.features;
        }

        if (ChunkStatus.heightmaps <= stage && chunk.chunkStatus < ChunkStatus.heightmaps)
        {
            ChunkBuilder.Heightmaps(chunk);
            chunk.chunkStatus = ChunkStatus.heightmaps;
        }

        if (ChunkStatus.light <= stage && chunk.chunkStatus < ChunkStatus.full)
        {
            WorldLight.InitialFillSkyLight(chunk);
            chunk.chunkStatus = ChunkStatus.light;
        }

        chunk.chunkStatus = ChunkStatus.full;
        helper.Noise.Cleanup(chunk.X, chunk.Z);
        return chunk;
    }

    public void Init(IWorld world)
    {
        helper = new GenHelper(world);
    }
}
