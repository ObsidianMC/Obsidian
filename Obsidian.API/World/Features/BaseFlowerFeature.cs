namespace Obsidian.API.World.Features;
public abstract class BaseFlowerFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:flower";

    public override required string Identifier { get; init; }

    public virtual int Tries { get; set; } = 128;
    public virtual int XZSpread { get; set; } = 7;
    public virtual int YSpread { get; set; } = 3;

    public abstract PlacedFeatureBase Feature { get; set; }

    public override abstract ValueTask Place(FeatureContext context);
}
