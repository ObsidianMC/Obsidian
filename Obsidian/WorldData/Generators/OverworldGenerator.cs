using Obsidian.API.World;
using Obsidian.WorldData.Decorators;
using Obsidian.WorldData.Generators.Overworld;

namespace Obsidian.WorldData.Generators;

public sealed class OverworldGenerator : ILevelGenerator
{
    private GenHelper helper;
    private ILevel world;

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

        if (ChunkGenStage.initialize_light <= stage && chunk.ChunkStatus < ChunkGenStage.initialize_light)
        {
            ChunkBuilder.Heightmaps(chunk);
            chunk.SetChunkStatus(ChunkGenStage.initialize_light);
        }

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.full)
        {
            Lighting.InitialFillSkyLight(chunk);
            await Lighting.LightFromNeighbors(chunk, world);
            chunk.SetChunkStatus(ChunkGenStage.light);
            await Lighting.LightToNeighbors(chunk, world);
        }

        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }

    public void Init(ILevel world)
    {
        this.world = world;
        helper = new GenHelper(world);
    }
}
