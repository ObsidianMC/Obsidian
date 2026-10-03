namespace Obsidian.WorldData.Structures;

/// <summary>
/// The decoration steps features and structures are placed in, in order, like vanilla's <c>GenerationStep.Decoration</c>.
/// </summary>
public enum DecorationStep
{
    RawGeneration,
    Lakes,
    LocalModifications,
    UndergroundStructures,
    SurfaceStructures,
    Strongholds,
    UndergroundOres,
    UndergroundDecoration,
    FluidSprings,
    VegetalDecoration,
    TopLayerModification
}

/// <summary>
/// How a structure reshapes the terrain around it during the noise step, like vanilla's <c>TerrainAdjustment</c>.
/// </summary>
public enum TerrainAdjustment
{
    /// <summary>The terrain is left alone.</summary>
    None,

    /// <summary>Fills the ground under the pieces and clears some air above, fading with distance (villages).</summary>
    BeardThin,

    /// <summary>Like <see cref="BeardThin"/> with a box-shaped falloff (ancient cities).</summary>
    BeardBox,

    /// <summary>Buries the pieces under terrain (strongholds, trail ruins).</summary>
    Bury,

    /// <summary>Encloses the pieces in terrain (trial chambers).</summary>
    Encapsulate
}
