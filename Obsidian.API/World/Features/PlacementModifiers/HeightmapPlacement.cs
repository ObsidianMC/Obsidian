﻿namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// Sets the Y coordinate to one block above the heightmap.
/// Returns the new position.
/// </summary>
public sealed class HeightmapPlacement : PlacementModifierBase
{
    public override string Type => "minecraft:heightmap";

    /// <summary>
    /// The heightmap to use. 
    /// One of MOTION_BLOCKING, MOTION_BLOCKING_NO_LEAVES, 
    /// OCEAN_FLOOR, OCEAN_FLOOR_WG, WORLD_SURFACE or WORLD_SURFACE_WG.
    /// </summary>
    public required string Heightmap { get; init; }

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
