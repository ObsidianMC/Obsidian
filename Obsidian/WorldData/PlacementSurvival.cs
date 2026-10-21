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
            default:
                return true;
        }
    }
}
