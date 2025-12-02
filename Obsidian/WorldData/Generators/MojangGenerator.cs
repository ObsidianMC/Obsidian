using Obsidian.WorldData.Generators.Mojang;


namespace Obsidian.WorldData.Generators;

internal class MojangGenerator : IWorldGenerator
{

    public string Id => "minecraft:mojang_generator";

    private ChunkBuilder _builder;
    private IWorld _world;

    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;

        chunk.SetChunkStatus(chunk.ChunkStatus == ChunkGenStage.empty ? ChunkGenStage.structure_references : chunk.ChunkStatus);

        if (ChunkGenStage.biomes <= stage && chunk.ChunkStatus < ChunkGenStage.biomes)
        {
            // Use multi-noise biome selection based on climate parameters
            _builder.PopulateBiomes(chunk);
            chunk.SetChunkStatus(ChunkGenStage.biomes);
        }

        if (ChunkGenStage.surface <= stage && chunk.ChunkStatus < ChunkGenStage.surface)
        {
            // Generate terrain using 3D density sampling with aquifer support
            _builder.Generate3DTerrain(chunk);

            // Apply surface rules to replace stone with grass, dirt, sand, etc.
            _builder.ApplySurfaceRules(chunk);

            chunk.SetChunkStatus(ChunkGenStage.surface);
        }

        if (ChunkGenStage.carvers <= stage && chunk.ChunkStatus < ChunkGenStage.carvers)
        {
            chunk.SetChunkStatus(ChunkGenStage.carvers);
        }

        if (ChunkGenStage.features <= stage && chunk.ChunkStatus < ChunkGenStage.features)
        {
            chunk.SetChunkStatus(ChunkGenStage.features);
        }

        if (ChunkGenStage.heightmaps <= stage && chunk.ChunkStatus < ChunkGenStage.heightmaps)
        {
            chunk.SetChunkStatus(ChunkGenStage.heightmaps);
        }

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.full)
        {
            Lighting.InitialFillSkyLight(chunk);
            await Lighting.LightFromNeighbors(chunk, _world);
            chunk.SetChunkStatus(ChunkGenStage.light);
            await Lighting.LightToNeighbors(chunk, _world);
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        return chunk;
    }
    public void Init(IWorld world)
    {
        _world = world;
        _builder = new ChunkBuilder(world);
    }
}
