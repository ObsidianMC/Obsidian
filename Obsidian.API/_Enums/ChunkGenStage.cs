namespace Obsidian.API;
public enum ChunkGenStage
{
    empty,
    structure_starts,
    structure_references,
    biomes,
    noise,
    surface,
    carvers,
    liquid_carvers,
    features,
    light,
    spawn,
    heightmaps,
    full
}
