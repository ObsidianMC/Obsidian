using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Pillar(JsonElement json)
    {
        var provider = this.Provider(json.GetProperty("block"));
        var direction = FeatureHelpers.ParseFace(json.GetProperty("direction").GetString());
        var chance = FloatOr(json, "chance_to_continue", 1.0f);
        var canReplace = json.TryGetProperty("can_replace", out var predicate)
            ? this.Predicate(predicate)
            : static (_, _) => true;
        var cap = json.TryGetProperty("cap_feature", out var capJson) ? this.Placed(capJson) : null;

        return (context, random, origin) =>
        {
            var level = context.Level;
            var position = origin;

            while (canReplace(level, position) && random.NextFloat() < chance)
            {
                if (level.IsOutsideBuildHeight(position.Y))
                {
                    break;
                }

                level.SetBlock(position, State(provider, level, random, position));
                position = position.Offset(direction);
            }

            // A cap runs even if the column placed no blocks, one step behind the stopped position.
            cap?.Place(context, random, position.Offset(direction.Opposite()));
            return true;
        };
    }

    private Feature ProjectedPatchySquare(JsonElement json)
    {
        var provider = this.Provider(json.GetProperty("block"));
        var projectThrough = this.Predicate(json.GetProperty("project_through"));
        var size = ValueProviders.Int(json.GetProperty("size"));
        var projection = json.GetProperty("max_projection_height").GetInt32();

        return (context, random, origin) =>
        {
            var level = context.Level;
            var radius = size(random);
            var bound = radius * radius + 1;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var loss = Math.Abs(dx) * Math.Abs(dz);

                    if (random.NextInt(bound) >= bound - loss)
                    {
                        continue;
                    }

                    var position = origin + new Vector(dx, 0, dz);
                    var remaining = projection;

                    // 26.3 tests the predicate before the height budget, so zero still permits one step.
                    while (projectThrough(level, position + Vector.Down))
                    {
                        position += Vector.Down;
                        remaining--;

                        if (remaining <= 0)
                        {
                            break;
                        }
                    }

                    var state = provider(level, random, position);

                    if (state is not null)
                    {
                        level.SetBlock(position, state);
                    }
                }
            }

            return true;
        };
    }
}
