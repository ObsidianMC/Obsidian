using Obsidian.API.World.Features;

namespace Obsidian.API;
public interface IWorldFeature : IFeature
{
    public ValueTask Place(FeatureContext context);

    public bool CanPlace(FeatureContext context);
}
