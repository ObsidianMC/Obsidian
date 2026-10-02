using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A vegetation patch whose enclosed ground positions become water (lush cave dripleaf pools), like vanilla's
/// WaterloggedVegetationPatchFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:waterlogged_vegetation_patch")]
public sealed class WaterloggedVegetationPatchFeature : VegetationPatchFeatureBase
{
    public override string Type => "minecraft:waterlogged_vegetation_patch";

    private static IBlock Water => field ??= BlocksRegistry.Get(Material.Water);

    protected override List<Vector> PlaceGroundPatch(IWorldGenLevel level, IRandomSource random, Vector origin, int radiusX, int radiusZ)
    {
        var ground = base.PlaceGroundPatch(level, random, origin, radiusX, radiusZ);

        // Exposure is decided for every position before any water is placed.
        var enclosed = RentPositions();
        foreach (var position in ground)
        {
            if (!IsExposed(level, position))
                enclosed.Add(position);
        }

        foreach (var position in enclosed)
            level.SetBlock(position, Water);

        var ordered = FeatureHelpers.JavaHashSetOrder(enclosed);
        ReturnPositions(enclosed);
        return ordered;
    }

    protected override bool PlaceVegetation(FeatureContext context, Vector ground)
    {
        if (!base.PlaceVegetation(context, ground + Vector.Down))
            return false;

        var level = context.Level;
        var state = level.GetBlock(ground);
        if (state.GetProperty("waterlogged") == "false")
            level.SetBlock(ground, state.WithProperty("waterlogged", true));

        return true;
    }

    private static bool IsExposed(IWorldGenLevel level, Vector position) =>
        IsExposedToward(level, position, BlockFace.North)
        || IsExposedToward(level, position, BlockFace.East)
        || IsExposedToward(level, position, BlockFace.South)
        || IsExposedToward(level, position, BlockFace.West)
        || IsExposedToward(level, position, BlockFace.Down);

    private static bool IsExposedToward(IWorldGenLevel level, Vector position, BlockFace face) =>
        !level.GetBlock(position.Offset(face)).IsFaceSturdy(face.Opposite());
}
