using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.HeightProviders;

/// <summary>
/// A fixed Y level. Feature data may also write a bare vertical anchor where a height provider is expected.
/// </summary>
[ConfiguredFeatureProperty("minecraft:constant")]
public sealed class ConstantHeightProvider : IHeightProvider
{
    public string Type { get; init; } = "minecraft:constant";

    public required VerticalAnchor Value { get; init; }

    public int Sample(IRandomSource random, WorldGenerationContext context) => this.Value.Resolve(context);
}
