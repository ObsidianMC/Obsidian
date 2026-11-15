namespace Obsidian.API;

/// <summary>
/// The generation steps a chunk goes through, in order; a later value means the chunk has gone further.
/// </summary>
public enum ChunkGenStage
{
    /// <summary>Nothing has been generated yet.</summary>
    empty,
    /// <summary>Structures that start in the chunk have been placed.</summary>
    structure_starts,
    /// <summary>References to structures from nearby chunks have been recorded.</summary>
    structure_references,
    /// <summary>Biomes have been filled in.</summary>
    biomes,
    /// <summary>The base terrain shape has been generated.</summary>
    noise,
    /// <summary>Surface blocks such as grass and sand have been placed.</summary>
    surface,
    /// <summary>Caves and canyons have been carved.</summary>
    carvers,
    /// <summary>Features such as trees, ores and structures have been placed.</summary>
    features,
    /// <summary>The chunk is ready for light to be calculated.</summary>
    initialize_light,
    /// <summary>Light has been calculated.</summary>
    light,
    /// <summary>The initial mobs have been spawned.</summary>
    spawn,
    /// <summary>Generation is complete.</summary>
    full
}
