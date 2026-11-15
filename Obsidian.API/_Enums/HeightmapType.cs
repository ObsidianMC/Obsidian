namespace Obsidian.API;
/// <summary>
/// The kinds of heightmap a chunk keeps. Each records, per column, the Y just above the highest block that counts for
/// it.
/// </summary>
public enum HeightmapType : int
{
    /// <summary>Counts any block that isn't air; used while the chunk generates.</summary>
    WorldSurfaceWG,
    /// <summary>Counts any block that isn't air.</summary>
    WorldSurface,

    /// <summary>Counts blocks that block movement; used while the chunk generates.</summary>
    OceanFloorWG,
    /// <summary>Counts blocks that block movement.</summary>
    OceanFloor,

    /// <summary>Counts blocks that block movement or hold a fluid.</summary>
    MotionBlocking,
    /// <summary>Counts blocks that block movement or hold a fluid, except leaves that hold no fluid.</summary>
    MotionBlockingNoLeaves,
}
