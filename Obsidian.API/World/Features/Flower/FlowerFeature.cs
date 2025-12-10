
using System.Text.Json.Serialization;

namespace Obsidian.API.World.Features.Flower;

public sealed class FlowerFeature : ConfiguredFeatureBase
{
    [JsonIgnore]
    public override string Type => "minecraft:flower";

    public override required string Identifier { get; init; }

    public required IIntProvider Tries { get; init; }
    public required IIntProvider XzSpread { get; init; }
    public required IIntProvider YSpread { get; init; }

    public required PlacementFlowerBlockFeature Feature { get; init; } 

    public override ValueTask Place(FeatureContext context) => default;
}


public sealed class PlacementFlowerBlockFeature : PlacedFeatureBase
{
    public required ConfiguredFeatureBase Feature { get; init; }

    public PlacementModifierBase[] Placement { get; init; } = [];

    public override ValueTask Place(FeatureContext context) => default;
}
