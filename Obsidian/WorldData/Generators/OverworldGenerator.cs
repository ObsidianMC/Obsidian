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

        chunk.SetChunkStatus(chunk.ChunkStatus == ChunkStatus.empty ? ChunkStatus.structure_references : chunk.ChunkStatus);

        if (ChunkStatus.biomes <= stage && chunk.ChunkStatus < ChunkStatus.biomes)
        {
            ChunkBuilder.Biomes(helper, chunk);
            chunk.SetChunkStatus(ChunkStatus.biomes);
        }

        if (ChunkStatus.surface <= stage && chunk.ChunkStatus < ChunkStatus.surface)
        {
            ChunkBuilder.Surface(helper, chunk);
            chunk.SetChunkStatus(ChunkStatus.surface);
        }

        if (ChunkStatus.carvers <= stage && chunk.ChunkStatus < ChunkStatus.carvers)
        {
            ChunkBuilder.CavesAndOres(helper, chunk);
            ChunkBuilder.UpdateWGHeightmap(chunk);
            chunk.SetChunkStatus(ChunkStatus.carvers);
        }

        if (ChunkStatus.features <= stage && chunk.ChunkStatus < ChunkStatus.features)
        {
            await OverworldDecorator.DecorateAsync(chunk, helper);
            chunk.SetChunkStatus(ChunkStatus.features);
        }

        if (ChunkStatus.heightmaps <= stage && chunk.ChunkStatus < ChunkStatus.heightmaps)
        {
            ChunkBuilder.Heightmaps(chunk);
            chunk.SetChunkStatus(ChunkStatus.heightmaps);
        }

        if (ChunkStatus.light <= stage && chunk.ChunkStatus < ChunkStatus.full)
        {
            WorldLight.InitialFillSkyLight(chunk);
            chunk.SetChunkStatus(ChunkStatus.light);
        }

        chunk.SetChunkStatus(ChunkStatus.full);
        return chunk;
    }

    public void Init(IWorld world)
    {
        helper = new GenHelper(world);
    }
}
