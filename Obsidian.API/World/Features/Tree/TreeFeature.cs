using Obsidian.API.World.Features.Tree.Decorators;
using Obsidian.API.World.Features.Tree.Placers;
using Obsidian.API.World.Features.Tree.Placers.Foliage;
using Obsidian.API.World.Features.Tree.Placers.Trunk;

namespace Obsidian.API.World.Features.Tree;
public sealed class TreeFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:tree";
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
