using Obsidian.API.World.Features.Tree.Placers.Trunk;
using System.Text.Json.Serialization;

namespace Obsidian.API.World.Features.Tree;
public sealed class TreeFeature : ConfiguredFeatureBase
{
    [JsonIgnore]
    public override string Type => "minecraft:tree";

    public override required string Identifier { get; init; }

    public bool IgnoreVines { get; set; }
    public bool ForceDirt { get; set; }

    public required TreeSizeBase MinimumSize { get; init; }
    public required TrunkPlacer TrunkPlacer { get; init; }
    public required FoliagePlacer FoliagePlacer { get; init; }

    public required IBlockStateProvider DirtProvider { get; init; }
    public required IBlockStateProvider TrunkProvider { get; init; }
    public required IBlockStateProvider FoliageProvider { get; init; }

    public RootPlacer? RootPlacer { get; init; }

    public List<DecoratorBase> Decorators { get; } = [];    

    public override ValueTask Place(FeatureContext context) => ValueTask.CompletedTask;
}
