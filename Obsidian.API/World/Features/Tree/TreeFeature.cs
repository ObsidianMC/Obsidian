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

    public List<DecoratorBase> Decorators { get; set; } = [];    

    public override async ValueTask Place(FeatureContext context)
    {
        //TODO USE RANDOM TO CALC TREE HEIGHT
        // baseHeight rand.Next(trunkPlacer.RandA + 1) + rand.Next(trunkPlacer.RandB + 1)
        var treeHeight = this.TrunkPlacer.BaseHeight;
        var foliageHeight = this.FoliagePlacer.GetHeight(treeHeight);

        var rootHeight = treeHeight - foliageHeight;

        var foliageRadius = this.FoliagePlacer.GetRadius(rootHeight);

        var blockPos = this.RootPlacer?.GetTrunkOrigin(context.PlacementLocation) ?? context.PlacementLocation;

        var min = Math.Min(blockPos.Y, context.PlacementLocation.Y);
        var max = Math.Max(blockPos.Y, context.PlacementLocation.Y) + treeHeight + 1;
    }
}
