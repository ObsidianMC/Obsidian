namespace Obsidian.WorldData.Features;

/// <summary>
/// A single vine block attached to the first supporting side (up, then horizontal faces in vanilla order), like vanilla's
/// VinesFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:vines")]
public sealed class VinesFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:vines";

    private static IBlock Vine => field ??= BlocksRegistry.Get(Material.Vine);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !level.GetBlock(origin).IsAir)
            return false;

        foreach (var face in FeatureHelpers.Directions)
        {
            if (face == BlockFace.Down || !BlockSurvival.CanAttachTo(level.GetBlock(origin.Offset(face)), face))
                continue;

            level.SetBlock(origin, Vine.WithProperty(FeatureHelpers.FaceName(face), true));
            return true;
        }

        return false;
    }
}
