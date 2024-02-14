using Obsidian.API.World.Features.Tree.Decorators;
using Obsidian.API.World.Features.Tree.Placers;
using Obsidian.API.World.Features.Tree.Placers.Foliage;
using Obsidian.API.World.Features.Tree.Placers.Trunk;

namespace Obsidian.API.World.Features.Tree;
public abstract class BaseTreeFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:tree";
    public virtual bool IgnoreVines { get; set; }
    public virtual bool ForceDirt { get; set; }

    public abstract TreeSizeBase MinimumSize { get; }

    public virtual RootPlacer? RootPlacer { get; }

    public abstract TrunkPlacer TrunkPlacer { get; }
    public abstract FoliagePlacer FoliagePlacer { get; }

    public List<DecoratorBase> Decorators { get; } = [];

    public override abstract ValueTask Place(FeatureContext context);

    public abstract void ConfigureProviders(Providers providers);
    public sealed class Providers
    {
        public IBlockStateProvider DirtProvider { get; set; } = default!;
        public IBlockStateProvider TrunkProvider { get; set; } = default!;
        public IBlockStateProvider FoliageProvider { get; set; } = default!;
    }
}
