namespace Obsidian.API.World.Features;
public abstract class PlacedFeatureBase : IFeature
{
    public abstract ValueTask Place(FeatureContext context);
}
