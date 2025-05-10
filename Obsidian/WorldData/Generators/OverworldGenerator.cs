using Obsidian.WorldData.Decorators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Generators;

public sealed class OverworldGenerator : IWorldGenerator
{
    private GenHelper helper;

    public string Id => "overworld";

    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkStatus stage = ChunkStatus.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;
        if (helper is null)
            throw new NullReferenceException("GenHelper must not be null. Call Init()");

        chunk.chunkStatus = chunk.chunkStatus == ChunkGenStage.empty ? ChunkGenStage.structure_references : chunk.chunkStatus;

        if (ChunkGenStage.biomes <= stage && chunk.chunkStatus < ChunkGenStage.biomes)
        {
            ChunkBuilder.Biomes(helper, chunk);
            chunk.chunkStatus = ChunkGenStage.biomes;
        }

        if (ChunkGenStage.surface <= stage && chunk.chunkStatus < ChunkGenStage.surface)
        {
            ChunkBuilder.Surface(helper, chunk);
            chunk.chunkStatus = ChunkGenStage.surface;
        }

        if (ChunkGenStage.carvers <= stage && chunk.chunkStatus < ChunkGenStage.carvers)
        {
            ChunkBuilder.CavesAndOres(helper, chunk);
            ChunkBuilder.UpdateWGHeightmap(chunk);
            chunk.chunkStatus = ChunkGenStage.carvers;
        }

        if (ChunkGenStage.features <= stage && chunk.chunkStatus < ChunkGenStage.features)
        {
            await OverworldDecorator.DecorateAsync(chunk, helper);
            chunk.chunkStatus = ChunkGenStage.features;
        }

        if (ChunkGenStage.heightmaps <= stage && chunk.chunkStatus < ChunkGenStage.heightmaps)
        {
            ChunkBuilder.Heightmaps(chunk);
            chunk.chunkStatus = ChunkGenStage.heightmaps;
        }

        if (ChunkGenStage.light <= stage && chunk.chunkStatus < ChunkGenStage.full)
        {
            WorldLight.InitialFillSkyLight(chunk);
            chunk.chunkStatus = ChunkGenStage.light;
        }

        chunk.chunkStatus = ChunkGenStage.full;
        return chunk;
    }

    public void Init(IWorld world)
    {
        helper = new GenHelper(world);
    }
}
