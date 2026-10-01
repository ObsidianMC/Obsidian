using Obsidian.API.World.Features;

namespace Obsidian.API;
public interface IWorldFeature : IFeature
{
    /// <summary>
    /// Places the feature. Returns whether anything was placed.
    /// </summary>
    public bool Place(FeatureContext context);
}
