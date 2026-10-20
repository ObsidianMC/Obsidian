using Obsidian.API.Blocks;
using Obsidian.Registries;
using Obsidian.WorldData.Features;

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

    // One member per Mojang class that owns an implemented placement override. Classes may share an implementation
    // object when their current vanilla algorithms are equivalent; block membership comes only from vanilla's class.
    internal static IBlockBehavior AbstractFurnaceBlock => HorizontalOpposite;
    internal static IBlockBehavior BarrelBlock => NearestOpposite;
    internal static IBlockBehavior BedBlock => Horizontal;
    internal static IBlockBehavior BeehiveBlock => HorizontalOpposite;
    internal static IBlockBehavior CampfireBlock => Horizontal;
    internal static IBlockBehavior ChestBlock => HorizontalOpposite;
    internal static IBlockBehavior ChiseledBookShelfBlock => HorizontalOpposite;
    internal static IBlockBehavior CommandBlock => NearestOpposite;
    internal static IBlockBehavior CopperChestBlock => HorizontalOpposite;
    internal static IBlockBehavior DecoratedPotBlock => Horizontal;
    internal static IBlockBehavior DispenserBlock => NearestOpposite;
    internal static IBlockBehavior DriedGhastBlock => HorizontalOpposite;
    internal static IBlockBehavior EnderChestBlock => HorizontalOpposite;
    internal static IBlockBehavior HopperBlock => ClickedFaceOpposite;
    internal static IBlockBehavior LecternBlock => HorizontalOpposite;
    internal static IBlockBehavior ShulkerBoxBlock => ClickedFace;
    internal static IBlockBehavior VaultBlock => HorizontalOpposite;

    /// <returns>The implemented behaviour of the vanilla class that owns this block's placement override, or null.</returns>
    public static IBlockBehavior? For(IBlock block) => BlocksRegistry.GetPlacementBehavior(block.RegistryId);

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
