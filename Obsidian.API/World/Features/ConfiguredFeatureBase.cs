
namespace Obsidian.API.World.Features;
public abstract class ConfiguredFeatureBase : IWorldFeature
{
    public abstract string Type { get; }
    public virtual bool CanPlace(FeatureContext context) => true;
    public abstract ValueTask Place(FeatureContext context);
}
