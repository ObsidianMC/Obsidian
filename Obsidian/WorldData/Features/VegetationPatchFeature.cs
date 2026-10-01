namespace Obsidian.WorldData.Features;

/// <summary>
/// Moss, clay and similar cave patches with vegetation on top, like vanilla's VegetationPatchFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:vegetation_patch")]
public sealed class VegetationPatchFeature : VegetationPatchFeatureBase
{
    public override string Type => "minecraft:vegetation_patch";
}
