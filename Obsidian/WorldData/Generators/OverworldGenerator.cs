using Obsidian.WorldData.Decorators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Generators;

public sealed class OverworldGenerator : IWorldGenerator
{
    private GenHelper helper;
    private IWorld world;

    public string Id => "overworld";

    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;
        if (helper is null)
            throw new NullReferenceException("GenHelper must not be null. Call Init()");

        chunk.SetChunkStatus(chunk.ChunkStatus == ChunkGenStage.empty ? ChunkGenStage.structure_references : chunk.ChunkStatus);

        if (ChunkGenStage.biomes <= stage && chunk.ChunkStatus < ChunkGenStage.biomes)
        {
            ChunkBuilder.Biomes(helper, chunk);
            chunk.SetChunkStatus(ChunkGenStage.biomes);
        }

        if (ChunkGenStage.surface <= stage && chunk.ChunkStatus < ChunkGenStage.surface)
        {
            ChunkBuilder.Surface(helper, chunk);
            chunk.SetChunkStatus(ChunkGenStage.surface);
        }

        if (ChunkGenStage.carvers <= stage && chunk.ChunkStatus < ChunkGenStage.carvers)
        {
            ChunkBuilder.CavesAndOres(helper, chunk);
            ChunkBuilder.UpdateWGHeightmap(chunk);
            chunk.SetChunkStatus(ChunkGenStage.carvers);
        }

        if (ChunkGenStage.features <= stage && chunk.ChunkStatus < ChunkGenStage.features)
        {
            await OverworldDecorator.DecorateAsync(chunk, helper);
            chunk.SetChunkStatus(ChunkGenStage.features);
        }

        if (ChunkGenStage.heightmaps <= stage && chunk.ChunkStatus < ChunkGenStage.heightmaps)
        {
            ChunkBuilder.Heightmaps(chunk);
            chunk.SetChunkStatus(ChunkGenStage.heightmaps);
        }

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.full)
        {
            WorldLight.InitialFillSkyLight(chunk);
            await WorldLight.PropagateFromNeighborsAsync(chunk, world);
            chunk.SetChunkStatus(ChunkGenStage.light);
            // After lighting this chunk, update any neighbors that were already generated
            await WorldLight.PropagateToNeighborsAsync(chunk, world);
        }

        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }

    public void Init(IWorld world)
    {
        this.world = world;
        helper = new GenHelper(world);
    }
}
