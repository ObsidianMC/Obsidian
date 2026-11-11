using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Placement;

/// <summary>
/// What vanilla's <c>BlockPlaceContext</c> works out from a player's click, read from Obsidian's
/// <see cref="BlockPlaceContext"/>: the directions the player looks, the click's height and the water being replaced.
/// </summary>
internal static class PlaceContexts
{
    /// <summary>
    /// Vanilla <c>getNearestLookingDirections</c>: all six directions from nearest to farthest from where the player
    /// looks, with the clicked face's opposite first unless the block replaces the clicked one.
    /// </summary>
    public static BlockFace[] NearestLookingDirections(this BlockPlaceContext context) =>
        PlacementDirections.NearestLooking(
            context.Player.Yaw,
            context.Player.Pitch,
            context.ClickedFace,
            context.ReplacesClicked);

    /// <summary>Vanilla <c>getNearestLookingVerticalDirection</c>: up when the player looks above the horizon.</summary>
    public static BlockFace NearestLookingVertical(this BlockPlaceContext context) =>
        context.Player.Pitch < 0 ? BlockFace.Up : BlockFace.Down;

    /// <summary>Vanilla <c>getRotation</c>: the player's yaw, in degrees.</summary>
    public static float Rotation(this BlockPlaceContext context) => context.Player.Yaw;

    /// <summary>Vanilla <c>isSecondaryUseActive</c>: the player sneaks.</summary>
    public static bool Sneaking(this BlockPlaceContext context) => context.Player.Sneaking;

    /// <summary>
    /// How far above the bottom of the block being placed the player clicked (vanilla
    /// <c>getClickLocation().y - getClickedPos().getY()</c>).
    /// </summary>
    public static double ClickHeight(this BlockPlaceContext context) =>
        context.ClickedPosition.Y + context.ClickedAt.Y - context.Position.Y;

    /// <summary>The block the placed block replaces.</summary>
    public static IBlock Replaced(this BlockPlaceContext context) => context.Level.GetBlock(context.Position);

    /// <summary>
    /// Vanilla <c>getFluidState(pos).is(Fluids.WATER)</c>: the block being replaced holds a water source, on its own or
    /// waterlogged.
    /// </summary>
    public static bool ReplacesWaterSource(this BlockPlaceContext context) =>
        context.Replaced().GetFluid() == FluidKind.Water;

    /// <summary>
    /// Vanilla <c>fluidState.is(FluidTags.WATER) &amp;&amp; fluidState.isFull()</c>: water of full height where the
    /// block goes, a source or falling water.
    /// </summary>
    public static bool ReplacesFullWater(this BlockPlaceContext context) => IsFullWater(context.Replaced());

    /// <summary>Whether a block holds water of full height, a source or falling water.</summary>
    public static bool IsFullWater(IBlock block) =>
        block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater && block.FluidAmount() == 8;

    /// <summary>
    /// The state waterlogged when it replaces a water source (vanilla's <c>setValue(WATERLOGGED, ...)</c> in
    /// <c>getStateForPlacement</c>).
    /// </summary>
    public static IBlock WaterloggedFor(this IBlock state, BlockPlaceContext context) =>
        state.WithProperty("waterlogged", context.ReplacesWaterSource());

    /// <summary>
    /// The same placement of another block: vanilla's item placements ask both a standing and a wall block for their
    /// state with one context.
    /// </summary>
    public static BlockPlaceContext WithBlock(this BlockPlaceContext context, IBlock block) => new()
    {
        Level = context.Level,
        Player = context.Player,
        Hand = context.Hand,
        Item = context.Item,
        Block = block,
        Position = context.Position,
        ClickedPosition = context.ClickedPosition,
        ClickedFace = context.ClickedFace,
        ClickedAt = context.ClickedAt,
        ReplacesClicked = context.ReplacesClicked,
        HorizontalDirection = context.HorizontalDirection,
        NearestLookDirection = context.NearestLookDirection
    };

    /// <summary>Vanilla <c>RotationSegment.convertToSegment</c>: one of 16 rotations, from a yaw in degrees.</summary>
    public static int RotationSegment(float degrees) => (int)Math.Floor(degrees / 22.5f + 0.5f) & 15;

    /// <summary>Vanilla <c>RotationSegment.convertToSegment(Direction)</c>: the rotation facing a direction.</summary>
    public static int RotationSegment(BlockFace direction) => HorizontalIndex(direction) * 4;

    /// <summary>Vanilla <c>Direction.get2DDataValue</c>: south, west, north, east as 0 to 3.</summary>
    public static int HorizontalIndex(BlockFace direction) => direction switch
    {
        BlockFace.South => 0,
        BlockFace.West => 1,
        BlockFace.North => 2,
        BlockFace.East => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };

    /// <summary>A direction's name, as block state properties name it.</summary>
    public static string Name(BlockFace direction) => FeatureHelpers.FaceName(direction);

    /// <summary>The axis a direction lies along, as <c>axis</c> properties name it.</summary>
    public static string AxisName(BlockFace direction) => direction switch
    {
        BlockFace.Up or BlockFace.Down => "y",
        BlockFace.North or BlockFace.South => "z",
        _ => "x"
    };

    /// <summary>Whether two directions lie along the same axis.</summary>
    public static bool SameAxis(BlockFace first, BlockFace second) => AxisName(first) == AxisName(second);
}
