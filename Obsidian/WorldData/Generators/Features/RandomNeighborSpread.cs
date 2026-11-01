using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature RandomNeighborSpread(JsonElement json)
    {
        var provider = this.Provider(json.GetProperty("block"));
        var neighbors = this.Blocks(json.GetProperty("accepted_neighbors"));
        var canReplace = this.Predicate(json.GetProperty("can_replace"));
        var attempts = this.Int(json.GetProperty("attempts"));
        var xzOffset = this.Int(json.GetProperty("xz_offset"));
        var yOffset = this.Int(json.GetProperty("y_offset"));

        return (context, random, origin) =>
        {
            var level = context.Level;
            level.SetBlock(origin, State(provider, level, random, origin));
            var count = attempts(random);

            for (var i = 0; i < count; i++)
            {
                var dx = xzOffset(random);
                var dy = yOffset(random);
                var dz = xzOffset(random);
                var position = origin + new Vector(dx, dy, dz);

                if (!canReplace(level, position))
                {
                    continue;
                }

                var matching = 0;

                foreach (var face in FeatureHelpers.Directions)
                {
                    if (neighbors.Contains(level.GetBlock(position.Offset(face)).RegistryId))
                    {
                        matching++;
                    }

                    if (matching > 1)
                    {
                        break;
                    }
                }

                if (matching == 1)
                {
                    level.SetBlock(position, State(provider, level, random, position));
                }
            }

            return true;
        };
    }
}
