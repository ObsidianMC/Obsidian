using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>BlockItem.canPlace</c> (<c>!mustSurvive() || state.canSurvive(level, pos)</c>), for the blocks whose
/// <c>canSurvive</c> Obsidian can evaluate on a live level. Rules are keyed by the vanilla block class, like
/// <see cref="Features.BlockSurvival"/>; any other block may be placed.
/// </summary>
internal static class PlacementSurvival
{
    /// <summary>
    /// Vanilla <c>canSurvive</c> for the blocks a <c>StandingAndWallBlockItem</c> places: a standing torch needs the
    /// centre of the block below, a wall torch the full face of the block behind, and a sign the solid block behind (a
    /// standing sign the solid block below). Other blocks are placed where they are.
    /// </summary>
    public static bool CanAttach(LevelBlockUpdates level, IBlock block, Vector position) => block.BlockClass() switch
    {
        "TorchBlock" => level.GetBlock(position + Vector.Down).IsTopCenterSturdy(),
        "WallTorchBlock" => level.GetBlock(position.Offset(FeatureHelpers.ParseFace(block.GetProperty("facing")).Opposite()))
            .IsFaceSturdy(FeatureHelpers.ParseFace(block.GetProperty("facing"))),
        "StandingSignBlock" => Signs.Survives(level, block, position),
        "WallSignBlock" => Signs.Survives(level, block, position),
        _ => true
    };

    /// <summary>
    /// <c>StemBlock</c>/<c>CropBlock</c> and its subclasses, and <c>CocoaBlock</c>: <c>BushBlock.mayPlaceOn</c> accepts only farmland, and a
    /// crop also needs a raw brightness of 8 (<c>CropBlock.hasSufficientLight</c>).
    /// </summary>
    public static bool CanPlace(Fluids.LevelFluids level, IBlock block, Vector position)
    {
        switch (block.BlockClass())
        {
            case "StemBlock":
                return level.GetBlock(position + Vector.Down).RegistryId == BlockIds.Farmland;
            case "CropBlock" or "CarrotBlock" or "PotatoBlock" or "BeetrootBlock" or "TorchflowerCropBlock" or "PitcherCropBlock":
                return level.GetBlock(position + Vector.Down).RegistryId == BlockIds.Farmland
                    && level.GetRawBrightness(position) >= 8;
            // CocoaBlock.canSurvive: the jungle log it's attached to, on the side its facing names.
            case "CocoaBlock":
                var facing = FeatureHelpers.ParseFace(block.GetProperty("facing"));
                return BlockSurvival.JungleLogs.Contains(level.GetBlock(position.Offset(facing)));
            case "SugarCaneBlock":
                return BlockSurvival.SugarCaneMayStay(level.GetBlock, position);
            case "CactusBlock":
                return BlockSurvival.CactusMayStay(level.GetBlock, position);
            case "BambooStalkBlock" or "BambooSaplingBlock":
                return BlockSurvival.BambooMayStay(level.GetBlock, position);
            case "KelpBlock":
                return BlockSurvival.KelpMayPlaceOn(level.GetBlock(position + Vector.Down));
            case "SeagrassBlock":
                return BlockSurvival.SeagrassMayPlaceOn(level.GetBlock(position + Vector.Down));
            default:
                return true;
        }
    }
}
