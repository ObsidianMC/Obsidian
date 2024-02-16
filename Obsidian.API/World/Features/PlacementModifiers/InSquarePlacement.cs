﻿namespace Obsidian.API.World.Features.PlacementModifiers;

/// <summary>
/// For both X and Z, it adds a random value between 0 and 15 (both inclusive). 
/// This is a shortcut for a random_offset modifier with y_spread set to 0 and xz_spread as a uniform int from 0 to 15. 
/// No additional fields.
/// </summary>
public sealed class InSquarePlacement : PlacementModifierBase
{
    public override string Type => "minecraft:in_square";

    protected override bool ShouldPlace(PlacementContext context) => throw new NotImplementedException();
}
