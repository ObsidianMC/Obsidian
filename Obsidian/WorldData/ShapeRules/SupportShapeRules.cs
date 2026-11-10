using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.ShapeRules;

// Shape rules of blocks that drop when what holds them goes. Each is the rule of the vanilla class that declares
// updateShape, bound by name in ShapeUpdateBehaviors.

/// <summary>
/// Vanilla <c>BaseTorchBlock.updateShape</c> (torches and redstone torches): drops without a floor whose top center
/// holds it, checked when the block below changes.
/// </summary>
internal sealed class StandingTorchShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction != BlockFace.Down)
            return state;

        return level.GetBlock(position + Vector.Down).IsTopCenterSturdy() ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>WallTorchBlock</c>, <c>RedstoneWallTorchBlock</c> and <c>LadderBlock</c> <c>updateShape</c>: drops when
/// the block behind loses its sturdy face, checked when that side changes.
/// </summary>
internal sealed class WallAttachedShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        if (direction.Opposite() != facing)
            return state;

        var behind = level.GetBlock(position + facing.Opposite().ToVector());
        return behind.IsFaceSturdy(facing) ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>FaceAttachedHorizontalDirectionalBlock.updateShape</c> (buttons, levers, grindstones): drops without a
/// sturdy face to hang on, checked when that side changes.
/// </summary>
internal sealed class FaceAttachedShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        // Vanilla's grindstone overrides canSurvive to always hold, so it never drops (captured:
        // grindstone_shape_* in more_blocks.json).
        if (state.Is(BlockIds.Grindstone))
            return state;

        var connected = state.GetProperty("face") switch
        {
            "ceiling" => BlockFace.Down,
            "floor" => BlockFace.Up,
            _ => FeatureHelpers.ParseFace(state.GetProperty("facing"))
        };

        if (connected.Opposite() != direction)
            return state;

        return level.GetBlock(position.Offset(direction)).IsFaceSturdy(connected) ? state : BlocksRegistry.Air;
    }
}

/// <summary>Vanilla <c>LanternBlock.updateShape</c>: drops without a block to stand on or hang from.</summary>
internal sealed class LanternShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var hanging = state.GetProperty("hanging") == "true";
        if (direction != (hanging ? BlockFace.Up : BlockFace.Down))
            return state;

        var support = level.GetBlock(position.Offset(direction));
        var held = hanging ? support.IsBottomCenterSturdy() : support.IsTopCenterSturdy();
        return held ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>WallBannerBlock</c> and <c>WallSignBlock</c> <c>updateShape</c>: drops when the block behind isn't solid,
/// checked when that side changes.
/// </summary>
internal sealed class WallMountedShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var behind = FeatureHelpers.ParseFace(state.GetProperty("facing")).Opposite();
        if (direction != behind)
            return state;

        return level.GetBlock(position.Offset(direction)).IsSolid() ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>BannerBlock</c> and <c>StandingSignBlock</c> <c>updateShape</c>: drops when the block below isn't solid.
/// </summary>
internal sealed class StandingMountedShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction != BlockFace.Down)
            return state;

        return level.GetBlock(position + Vector.Down).IsSolid() ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>CeilingHangingSignBlock.updateShape</c>: drops when the block above can't hold its center from below.
/// </summary>
internal sealed class CeilingHangingSignShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction != BlockFace.Up)
            return state;

        var above = level.GetBlock(position + Vector.Up);
        return above.IsFaceSturdy(BlockFace.Down, SupportType.Center) ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>WallHangingSignBlock.updateShape</c>: drops when neither end of its bar is held, by a sturdy face or an
/// aligned wall hanging sign, checked when either end changes.
/// </summary>
internal sealed class WallHangingSignShapeRule : SharedShapeRule
{
    private static readonly WorldgenTags.BlockGroup wallHangingSigns = WorldgenTags.Blocks("wall_hanging_signs");

    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        var sideways = FeatureHelpers.ParseFace(state.GetProperty("facing")).ClockWise();
        if (!SameAxis(direction, sideways) || IsSupported(level, state, position, sideways))
            return state;

        return BlocksRegistry.Air;
    }

    private static bool IsSupported(IShapeLevel level, IBlock state, Vector position, BlockFace sideways)
    {
        foreach (var side in new[] { sideways, sideways.Opposite() })
        {
            var neighbor = level.GetBlock(position.Offset(side));
            if (wallHangingSigns.Contains(neighbor))
            {
                var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
                var neighborFacing = FeatureHelpers.ParseFace(neighbor.GetProperty("facing"));
                if (SameAxis(facing, neighborFacing))
                    return true;
            }
            else if (neighbor.IsFaceSturdy(side.Opposite()))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Vanilla <c>AttachedStemBlock.updateShape</c>: a stem whose fruit goes becomes a fully grown stem again, and one
/// without farmland drops.
/// </summary>
internal sealed class AttachedStemShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction == FeatureHelpers.ParseFace(state.GetProperty("facing")) && !IsFruit(state, neighbor))
            return BlockStateProperties.GetState(StemFor(state)).WithProperty("age", 7);

        return state.CanSurvive(level, position) ? state : BlocksRegistry.Air;
    }

    private static bool IsFruit(IBlock stem, IBlock neighbor) =>
        neighbor.RegistryId == (stem.Is(BlockIds.AttachedMelonStem) ? BlockIds.Melon : BlockIds.Pumpkin);

    private static string StemFor(IBlock attached) =>
        attached.Is(BlockIds.AttachedMelonStem) ? "minecraft:melon_stem" : "minecraft:pumpkin_stem";
}

/// <summary>
/// The <c>updateShape</c> of <c>VegetationBlock</c>, carpets, seagrass, snow layers, hanging roots, spore blossoms and
/// cocoa: whenever a neighbour changes, the block turns into air if it can no longer survive where it is.
/// </summary>
internal sealed class SurvivalShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        // Only mushrooms' survival reads the light.
        var brightness = state.RegistryId is BlockIds.BrownMushroom or BlockIds.RedMushroom
            ? level.GetRawBrightness(position)
            : 15;
        return state.CanSurvive(level, position, brightness) ? state : BlocksRegistry.Air;
    }
}

/// <summary>
/// Vanilla <c>HangingMossBlock.updateShape</c>: drops when what it hangs from goes, and is a tip unless more moss
/// hangs below.
/// </summary>
internal sealed class HangingMossShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (direction == BlockFace.Up && !state.CanSurvive(level, position))
            return BlocksRegistry.Air;

        return state.WithProperty("tip", level.GetBlock(position + Vector.Down).RegistryId != state.RegistryId);
    }
}

/// <summary>
/// Vanilla <c>MossyCarpetBlock.updateShape</c> (pale moss carpets): sides follow the walls around it.
/// </summary>
internal sealed class MossyCarpetShapeRule : SharedShapeRule
{
    public override IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor) =>
        Generators.Features.FeatureRegistry.UpdateMossCarpetShape(state, level, position);
}
