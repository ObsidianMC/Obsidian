namespace Obsidian.WorldData.Features;

/// <summary>
/// Stands in for a configured feature whose type isn't implemented yet, so the generated registries stay complete.
/// Placing it does nothing. The source generator emits a warning for every use.
/// </summary>
public sealed class UnsupportedFeature : ConfiguredFeatureBase
{
    public UnsupportedFeature(string type) => this.Type = type;

    public override string Type { get; }

    public override bool Place(FeatureContext context) => false;
}
