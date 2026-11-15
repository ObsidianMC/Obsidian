using Obsidian.API.World.Features;

namespace Obsidian.API;
/// <summary>
/// A worldgen feature that places blocks in a level, like a tree or an ore vein.
/// </summary>
public interface IWorldFeature : IFeature
{
    /// <summary>
    /// Places the feature. Returns whether anything was placed.
    /// </summary>
    public bool Place(FeatureContext context);
}
