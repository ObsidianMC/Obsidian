namespace Obsidian.API.World.Features;
public abstract class PlacedFeatureBase : IFeature
{
    public required string Type { get; init; }

    public abstract ValueTask Place(FeatureContext context);
}
