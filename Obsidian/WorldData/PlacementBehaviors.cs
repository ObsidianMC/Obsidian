using Obsidian.API.Blocks;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>Block.getStateForPlacement</c>. Member names match
/// the Mojang declaring classes dumped by <c>VanillaDumper</c>; the block generator binds blocks directly to them.
/// </summary>
internal static class PlacementBehaviors
{
    private static readonly IBlockBehavior HorizontalOpposite = new HorizontalFacing(opposite: true);
    private static readonly IBlockBehavior Horizontal = new HorizontalFacing(opposite: false);
    private static readonly IBlockBehavior NearestOpposite = new NearestFacing();
    private static readonly IBlockBehavior ClickedFace = new ClickedFaceFacing(opposite: false);
    private static readonly IBlockBehavior ClickedFaceOpposite = new ClickedFaceFacing(opposite: true);
    private static readonly IBlockBehavior Leaves = new LeavesBlockBehavior.Placement();
    private static readonly IBlockBehavior BambooStalk = new BambooStalkPlacement();
    private static readonly IBlockBehavior Crafter = new CrafterPlacement();
    private static readonly IBlockBehavior Rail = new RailPlacement();

    // One member per Mojang class that owns an implemented placement override. Classes may share an implementation
    // object when their current vanilla algorithms are equivalent; block membership comes only from vanilla's class.
    internal static IBlockBehavior AbstractFurnaceBlock => HorizontalOpposite;
    internal static IBlockBehavior BarrelBlock => NearestOpposite;
    internal static IBlockBehavior BaseRailBlock => Rail;
    internal static IBlockBehavior BambooStalkBlock => BambooStalk;
    internal static IBlockBehavior BedBlock => Horizontal;
    internal static IBlockBehavior BeehiveBlock => HorizontalOpposite;
    internal static IBlockBehavior CampfireBlock => Horizontal;
    internal static IBlockBehavior CrafterBlock => Crafter;
    internal static IBlockBehavior ChestBlock => HorizontalOpposite;
    internal static IBlockBehavior ChiseledBookShelfBlock => HorizontalOpposite;
    internal static IBlockBehavior CommandBlock => NearestOpposite;
    internal static IBlockBehavior CopperChestBlock => HorizontalOpposite;
    internal static IBlockBehavior DecoratedPotBlock => Horizontal;
    internal static IBlockBehavior DispenserBlock => NearestOpposite;
    internal static IBlockBehavior DriedGhastBlock => HorizontalOpposite;
    internal static IBlockBehavior EnderChestBlock => HorizontalOpposite;
    internal static IBlockBehavior HopperBlock => ClickedFaceOpposite;
    internal static IBlockBehavior IronBarsBlock { get; } = new IronBarsPlacementBehavior();
    internal static IBlockBehavior LecternBlock => HorizontalOpposite;
    internal static IBlockBehavior LeavesBlock => Leaves;
    internal static IBlockBehavior ShulkerBoxBlock => ClickedFace;
    internal static IBlockBehavior VaultBlock => HorizontalOpposite;

    /// <returns>The implemented behaviour of the vanilla class that owns this block's placement override, or null.</returns>
    public static IBlockBehavior? For(IBlock block) => BlocksRegistry.GetPlacementBehavior(block.RegistryId);

    /// <summary>
    /// Vanilla <c>BambooStalkBlock.getStateForPlacement</c>: a stalk on bamboo or a sapling, a sapling on other
    /// plantable ground unless bamboo stands two below, and nothing in fluid or on unplantable ground.
    /// </summary>
    private sealed class BambooStalkPlacement : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var position = context.Position;
            if (context.Level.GetBlock(position).GetFluid() != FluidKind.Empty)
                return null;

            if (!BlockSurvival.BambooMayStay(context.Level.GetBlock, position))
                return null;

            var below = context.Level.GetBlock(position + Vector.Down);
            if (below.Is(BlockIds.BambooSapling))
                return context.Block.WithProperty("leaves", "none");

            if (below.Is(BlockIds.Bamboo))
                return context.Block.WithProperty("leaves", below.GetProperty("leaves") == "none" ? "none" : "small");

            var belowBelow = context.Level.GetBlock(position + Vector.Down * 2);
            return belowBelow.Is(BlockIds.Bamboo)
                ? context.Block.WithProperty("leaves", belowBelow.GetProperty("leaves"))
                : BlocksRegistry.GetDefault(BlockIds.BambooSapling);
        }
    }

    private sealed class HorizontalFacing(bool opposite) : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var direction = opposite ? context.HorizontalDirection.Opposite() : context.HorizontalDirection;
            return context.Block.WithProperty("facing", FeatureHelpers.FaceName(direction));
        }
    }

    private sealed class NearestFacing : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context) =>
            context.Block.WithProperty("facing", FeatureHelpers.FaceName(context.NearestLookDirection.Opposite()));
    }

    private sealed class ClickedFaceFacing(bool opposite) : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var face = context.ClickedFace;
            if (opposite)
                face = face is BlockFace.Up or BlockFace.Down ? BlockFace.Down : face.Opposite();

            return context.Block.WithProperty("facing", FeatureHelpers.FaceName(face));
        }
    }
}
