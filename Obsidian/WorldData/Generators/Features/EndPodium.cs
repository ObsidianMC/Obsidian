using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature EndPodium(JsonElement json)
    {
        var active = json.TryGetProperty("active", out var value) && value.GetBoolean();

        return (context, _, origin) =>
        {
            var level = context.Level;

            // Vanilla 26.3 rounds the fountain by full 3D distance, including the floor and cleared dome.
            foreach (var position in FeatureHelpers.BetweenClosed(
                origin + new Vector(-4, -1, -4), origin + new Vector(4, 32, 4)))
            {
                var distance = FeatureHelpers.DistSqr(position, origin);

                if (distance >= 3.5 * 3.5)
                {
                    continue;
                }

                var inside = distance < 2.5 * 2.5;
                IBlock state;

                if (position.Y < origin.Y)
                {
                    state = inside ? BlocksRegistry.Bedrock : BlocksRegistry.EndStone;
                }
                else if (position.Y > origin.Y)
                {
                    state = BlocksRegistry.Air;
                }
                else if (!inside)
                {
                    state = BlocksRegistry.Bedrock;
                }
                else
                {
                    state = active ? BlocksRegistry.EndPortal : BlocksRegistry.Air;
                }

                level.SetBlock(position, state);
            }

            for (var y = 0; y < 4; y++)
            {
                level.SetBlock(origin + new Vector(0, y, 0), BlocksRegistry.Bedrock);
            }

            foreach (var face in FeatureHelpers.Horizontal)
            {
                var torch = BlocksRegistry.WallTorch.WithProperty("facing", FeatureHelpers.FaceName(face));
                level.SetBlock((origin + new Vector(0, 2, 0)).Offset(face), torch);
            }

            return true;
        };
    }
}
