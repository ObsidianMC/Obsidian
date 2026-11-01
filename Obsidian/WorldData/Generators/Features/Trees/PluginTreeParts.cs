using Obsidian.API.World.Generator.RandomSources;
using System.Diagnostics;
using System.Text.Json;
using ApiFeatureSize = Obsidian.API.World.Features.Trees.FeatureSize;
using ApiFoliageAttachment = Obsidian.API.World.Features.Trees.FoliageAttachment;
using ApiFoliagePlacer = Obsidian.API.World.Features.Trees.FoliagePlacer;
using ApiRootPlacer = Obsidian.API.World.Features.Trees.RootPlacer;
using ApiTreeDecorator = Obsidian.API.World.Features.Trees.TreeDecorator;
using ApiTrunkPlacer = Obsidian.API.World.Features.Trees.TrunkPlacer;
using TreeContext = Obsidian.API.World.Features.Trees.TreeContext;
using TreeDecoratorContext = Obsidian.API.World.Features.Trees.TreeDecoratorContext;

namespace Obsidian.WorldData.Generators.Features.Trees;

// The tree parts plugins registered (RegisterWorldgenType with the API's TrunkPlacer, FoliagePlacer, RootPlacer,
// FeatureSize and TreeDecorator), as parts of a 26.3 tree. They see the tree being placed through the API's
// TreeContext and TreeDecoratorContext (see PluginTreeContext), and draw from the tree's random like vanilla's parts.

/// <summary>A plugin's trunk placer.</summary>
/// <param name="json">The placer's JSON, for the <c>base_height</c> and random heights every trunk placer has.</param>
internal sealed class PluginTrunkPlacer(ApiTrunkPlacer plugin, JsonElement json) : TrunkPlacer(json)
{
    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var attachments = plugin.PlaceTrunk(tree.PluginContext, freeTreeHeight, origin);

        return attachments.ConvertAll(PluginTreeContext.FromApi);
    }

    // The free space the tree needs is the plugin's: its valid positions, or a log.
    protected override bool ValidTreePos(TreeTags tags, IWorldGenLevel level, Vector position) =>
        plugin.IsFree(level, position);
}

/// <summary>A plugin's foliage placer.</summary>
/// <remarks>
/// The plugin samples its own radius and offset, the offset as it places each attachment's foliage, so the base
/// class's providers draw nothing.
/// </remarks>
internal sealed class PluginFoliagePlacer(ApiFoliagePlacer plugin) : FoliagePlacer(static _ => 0, static _ => 0)
{
    public override int FoliageHeight(IRandomSource random, int treeHeight) =>
        plugin.GetFoliageHeight(random, treeHeight);

    public override int FoliageRadius(IRandomSource random, int trunkHeight) =>
        plugin.GetFoliageRadius(random, trunkHeight);

    protected override void CreateFoliage(
        TreePlacement tree,
        int freeTreeHeight,
        FoliageAttachment attachment,
        int foliageHeight,
        int foliageRadius,
        int offset)
    {
        var pluginAttachment = PluginTreeContext.ToApi(attachment);

        plugin.CreateFoliage(tree.PluginContext, freeTreeHeight, pluginAttachment, foliageHeight, foliageRadius);
    }

    // Only the base class's leaf rows ask, which the plugin's foliage doesn't use.
    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        throw new UnreachableException();
}

/// <summary>A plugin's root placer.</summary>
internal sealed class PluginRootPlacer(ApiRootPlacer plugin) : RootPlacer
{
    public override Vector TrunkOrigin(Vector origin, IRandomSource random) => plugin.GetTrunkOrigin(origin, random);

    public override bool PlaceRoots(TreePlacement tree, Vector origin, Vector trunkOrigin) =>
        plugin.PlaceRoots(tree.PluginContext, origin, trunkOrigin);
}

/// <summary>A plugin's feature size.</summary>
internal sealed class PluginFeatureSize(ApiFeatureSize plugin)
    : FeatureSize(plugin.MinClippedHeight == ApiFeatureSize.NoMinClippedHeight ? null : plugin.MinClippedHeight)
{
    public override int SizeAtHeight(int height, int y) => plugin.GetSizeAtHeight(height, y);
}

/// <summary>A plugin's tree decorator, which sees the tree's sorted positions like vanilla's decorators.</summary>
internal sealed class PluginTreeDecorator(ApiTreeDecorator plugin) : TreeDecorator
{
    public override void Place(TreeDecoration context) => plugin.Place(new DecoratorView(context));

    private sealed class DecoratorView(TreeDecoration decoration) : TreeDecoratorContext
    {
        public override IWorldGenLevel Level => decoration.Level;

        public override IRandomSource Random => decoration.Random;

        public override WorldGenerationContext Generation { get; } = new(
            decoration.Context.GeneratorMinY,
            decoration.Context.GeneratorHeight);

        public override IReadOnlyList<Vector> Logs => decoration.Logs;

        public override IReadOnlyList<Vector> Leaves => decoration.Leaves;

        public override IReadOnlyList<Vector> Roots => decoration.Roots;

        public override void SetBlock(Vector position, IBlock block) => decoration.SetBlock(position, block);
    }
}

/// <summary>
/// What plugins' tree parts see of a 26.3 tree being placed: its level, random and providers, and setters that record
/// the tree's positions like its own parts' do.
/// </summary>
/// <remarks>
/// 26.3 replaced 1.21.11's <c>dirt_provider</c> and <c>force_dirt</c> with <c>below_trunk_provider</c>, whose rules
/// decide what goes under the trunk. So the dirt is always forced here, and the provider is the below-trunk one. Where
/// it has no state, vanilla places nothing: the trunk placer's next <see cref="SetLog"/> at that position, of the block
/// already there, which the provider gave instead, is dropped.
/// </remarks>
internal sealed class PluginTreeContext : TreeContext
{
    private readonly TreePlacement tree;

    // Where the below-trunk provider last had no state.
    private Vector? keptBelowTrunk;

    public PluginTreeContext(TreePlacement tree)
    {
        this.tree = tree;
        this.Generation = new WorldGenerationContext(tree.Context.GeneratorMinY, tree.Context.GeneratorHeight);
        this.TrunkProvider = new Provider(tree, tree.Config.TrunkProvider);
        this.FoliageProvider = new Provider(tree, tree.Config.FoliageProvider);
        this.DirtProvider = new BelowTrunkProvider(this);
    }

    public override IWorldGenLevel Level => this.tree.Level;

    public override IRandomSource Random => this.tree.Random;

    public override WorldGenerationContext Generation { get; }

    public override IBlockStateProvider TrunkProvider { get; }

    public override IBlockStateProvider FoliageProvider { get; }

    public override IBlockStateProvider DirtProvider { get; }

    public override bool ForceDirt => true;

    public override void SetLog(Vector position, IBlock block)
    {
        var kept = this.keptBelowTrunk == position;
        this.keptBelowTrunk = null;

        if (!kept)
            this.tree.SetLog(position, block);
    }

    public override void SetRoot(Vector position, IBlock block) => this.tree.SetRoot(position, block);

    public override void SetFoliage(Vector position, IBlock block) => this.tree.SetFoliage(position, block);

    public override bool IsFoliageSet(Vector position) => this.tree.IsFoliageSet(position);

    public static FoliageAttachment FromApi(ApiFoliageAttachment attachment) =>
        new(attachment.Position, attachment.RadiusOffset, attachment.DoubleTrunk);

    public static ApiFoliageAttachment ToApi(FoliageAttachment attachment) =>
        new(attachment.Position, attachment.RadiusOffset, attachment.DoubleTrunk);

    // A rule-based provider without a state keeps the block, as the tree's own parts do.
    private sealed class Provider(TreePlacement tree, StateProvider provider) : IBlockStateProvider
    {
        public string Type => "obsidian:tree_provider";

        public IBlock GetState(IRandomSource random, Vector position) =>
            FeatureRegistry.State(provider, tree.Level, random, position);
    }

    private sealed class BelowTrunkProvider(PluginTreeContext context) : IBlockStateProvider
    {
        public string Type => "obsidian:below_trunk_provider";

        public IBlock GetState(IRandomSource random, Vector position)
        {
            var tree = context.tree;
            var state = tree.Config.BelowTrunkProvider(tree.Level, random, position);

            if (state is not null)
                return state;

            context.keptBelowTrunk = position;
            return tree.Level.GetBlock(position);
        }
    }
}
