using System.Text.Json.Serialization;

namespace Obsidian.WorldData.Features;

[ConfiguredFeature("minecraft:simple_block")]
public sealed class SimpleBlockConfiguredFeature : ConfiguredFeatureBase
{
    [JsonIgnore]
    public override string Type => "minecraft:simple_block";

    public required SimpleBlockFeatureConfig Config { get; init; }

    public override string Identifier { get; init; }

    public override ValueTask Place(FeatureContext context) => default;
}

public readonly record struct SimpleBlockFeatureConfig
{
    public required IBlockStateProvider ToPlace { get; init; }

    public bool ScheduleTick { get; init; }
}

