using Obsidian.API.World.Features;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    // 26.3 SimpleBlockFeature calls MossyCarpetBlock.placeAt with the region random, independently of its provider.
    private static void PlaceMossCarpet(IWorldGenLevel level, Vector position)
    {
        var carpet = BlocksRegistry.PaleMossCarpet;
        var baseState = UpdateMossCarpet(carpet, level, position);
        level.SetBlock(position, baseState);

        var above = position + Vector.Up;
        var existing = level.GetBlock(above);
        var isCarpet = existing.RegistryId == carpet.RegistryId;

        if (isCarpet && existing.GetProperty("bottom") == "true" || !isCarpet && !existing.CanBeReplaced())
        {
            return;
        }

        var topper = UpdateMossCarpet(carpet.WithProperty("bottom", false), level, above);
        var hasSide = false;

        foreach (var face in FeatureHelpers.Horizontal)
        {
            var property = face.ToString().ToLowerInvariant();

            if (topper.GetProperty(property) == "none")
            {
                continue;
            }

            if (!level.Random.NextBoolean())
            {
                topper = topper.WithProperty(property, "none");
            }
            else
            {
                hasSide = true;
            }
        }

        if (!hasSide || topper.IsSameState(existing))
        {
            return;
        }

        level.SetBlock(above, topper);
        level.SetBlock(position, UpdateMossCarpet(baseState, level, position));
    }

    // placeAt forces a low side at each supporting face, then joins it to the carpet above or below.
    private static IBlock UpdateMossCarpet(IBlock state, IWorldGenLevel level, Vector position)
    {
        var above = level.GetBlock(position + Vector.Up);
        var below = level.GetBlock(position + Vector.Down);
        var isBase = state.GetProperty("bottom") == "true";

        foreach (var face in FeatureHelpers.Horizontal)
        {
            var property = face.ToString().ToLowerInvariant();
            var support = level.GetBlock(position.Offset(face));
            var side = "none";

            if (BlockSurvival.CanAttachTo(support, face))
            {
                side = "low";

                if (above.RegistryId == state.RegistryId
                    && above.GetProperty("bottom") == "false"
                    && above.GetProperty(property) != "none")
                {
                    side = "tall";
                }

                if (!isBase && below.RegistryId == state.RegistryId && below.GetProperty(property) == "none")
                {
                    side = "none";
                }
            }

            state = state.WithProperty(property, side);
        }

        return state;
    }
}
