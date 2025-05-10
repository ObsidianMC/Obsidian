namespace Obsidian.API.World.Features;
public abstract class PlacedFeatureBase : IFeature
{
    public abstract ConfiguredFeatureBase Feature { get; set; }

    public List<PlacementModifierBase> Placement { get; } = [];
}
